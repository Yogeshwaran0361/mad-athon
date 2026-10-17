using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using ReworkGuard.Application.Abstractions;
using ReworkGuard.Application.Auth;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;
using ReworkGuard.Infrastructure.Auth;
using ReworkGuard.Infrastructure.Persistence;

namespace ReworkGuard.Tests.Support.Perf;

/// <summary>Who to sign in as and which rows to hit once the volume data set is in place.</summary>
public sealed record PerfSeedResult(
    IReadOnlyDictionary<string, string> Users,
    int BusyEmployeeId,
    int TaskWithBugsId,
    int LatestLockedRunId,
    long PayslipId,
    IReadOnlyDictionary<string, long> RowCounts,
    IReadOnlyList<(string Phase, TimeSpan Elapsed)> Phases,
    IReadOnlyList<TimeSpan> PayrollGenerateTimings);

/// <summary>
/// Deterministic volume data for performance work: 24 months of history (2024-04 to 2026-03) for a mid-sized company.
/// Employees, rate history, projects and modules go through the domain model and EF; tasks, assignments, state
/// history, time logs, bugs, notifications and synthetic audit events are bulk-inserted with multi-row INSERTs
/// (audit rows go through the hash-chain trigger like any other insert); deduction lines are drafted by the real
/// <see cref="DeductionLedger"/> and moved through the domain lifecycle; every past month is then run through the
/// real payroll API (generate, review, approve, finalize, lock), so payroll runs, items and payslips are genuine.
/// The random generator has a fixed seed, so the same scale always yields the same data.
/// </summary>
public sealed class PerfSeeder
{
    public const int Seed = 20240401;

    public static readonly DateTime NowUtc = new(2026, 3, 28, 9, 0, 0, DateTimeKind.Utc);

    public static readonly DateOnly Today = DateOnly.FromDateTime(NowUtc);

    public static readonly YearMonth FirstMonth = new(2024, 4);

    public static readonly YearMonth CurrentMonth = new(2026, 3);

    private static readonly RootCause[] RootCauses =
    [
        .. Enumerable.Repeat(RootCause.OwnDefect, 60), .. Enumerable.Repeat(RootCause.SpecAmbiguity, 12),
        .. Enumerable.Repeat(RootCause.NewRequirement, 10), .. Enumerable.Repeat(RootCause.QaMiss, 10),
        .. Enumerable.Repeat(RootCause.ExternalIssue, 8),
    ];

    private readonly string _connectionString;
    private readonly ApiFactory _api;
    private readonly PerfScale _scale;
    private readonly Action<string> _log;
    private readonly Random _random = new(Seed);
    private readonly List<(string, TimeSpan)> _phases = [];
    private readonly Dictionary<(int, DateOnly), decimal> _dayHours = [];

    private readonly List<Employee> _employees = [];
    private readonly List<int> _deliverers = [];
    private readonly List<int> _leads = [];
    private readonly List<int> _managers = [];
    private readonly List<int> _qa = [];
    private readonly List<(int Id, int WarrantyDays)> _modules = [];
    private readonly List<TaskModel> _tasks = [];
    private readonly List<BugModel> _bugs = [];
    private readonly List<string> _logRows = [];
    private readonly List<AuditRow> _audit = [];
    private readonly Dictionary<string, string> _users = [];

    public PerfSeeder(string connectionString, ApiFactory api, PerfScale scale, Action<string> log)
    {
        _connectionString = connectionString;
        _api = api;
        _scale = scale;
        _log = log;
    }

    public async Task<PerfSeedResult> SeedAsync()
    {
        _api.Clock.UtcNow = NowUtc;
        await Phase("employees, projects, modules (EF)", SeedPeopleAndProjectsAsync);
        await Phase("tasks, logs, bugs, history (bulk SQL)", SeedWorkAsync);
        await Phase("audit events + notifications (bulk SQL, chain trigger)", SeedAuditAndNotificationsAsync);
        await Phase("deduction lines (DeductionLedger)", DraftDeductionsAsync);
        await Phase("deduction lifecycle (domain)", MoveDeductionsAsync);
        var generateTimings = new List<TimeSpan>();
        await Phase("payroll runs 2024-04..2026-02 (API)", () => RunPastPayrollsAsync(generateTimings));
        return await Phase("summary", () => SummariseAsync(generateTimings));
    }

    private async Task Phase(string name, Func<Task> action) => await Phase(name, async () =>
    {
        await action();
        return 0;
    });

    private async Task<T> Phase<T>(string name, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        _phases.Add((name, stopwatch.Elapsed));
        _log($"seed: {name} in {stopwatch.Elapsed.TotalSeconds:0.0}s");
        return result;
    }

    // ---------------------------------------------------------------- people and projects

    private async Task SeedPeopleAndProjectsAsync()
    {
        var n = _scale.Employees;
        var finance = Math.Max(2, n / 50);
        var managers = Math.Max(3, n / 25);
        var leads = Math.Max(4, n / 12);
        var qa = Math.Max(4, n / 10);
        var hash = new IdentityPasswordHasher().Hash(TestData.Password);

        await using var db = MySqlContainerFixture.CreateContext(_connectionString);
        var counter = 0;
        Employee Make(Role role, string? email, int? managerId)
        {
            counter++;
            var payRoll = _random.NextDouble();
            var delivery = role is Role.Employee or Role.QA;
            var (mode, rate) = !delivery || payRoll < 0.6
                ? (PayMode.Monthly, (decimal)(delivery ? _random.Next(60, 181) * 500 : _random.Next(160, 401) * 500))
                : payRoll < 0.85
                    ? (PayMode.Hourly, (decimal)_random.Next(25, 91) * 10)
                    : (PayMode.Daily, (decimal)_random.Next(20, 61) * 100);
            var employee = new Employee(
                $"P{counter:D4}",
                $"{role} {counter:D4}",
                email ?? $"{role.ToString().ToLowerInvariant()}{counter:D4}@perf.local",
                role,
                mode,
                mode == PayMode.Monthly ? rate : null,
                mode == PayMode.Daily ? rate : null,
                mode == PayMode.Hourly ? rate : null,
                managerId);
            employee.SetPasswordHash(hash);
            db.Employees.Add(employee);
            _employees.Add(employee);
            if (email is not null)
            {
                _users[email.Split('@')[0]] = email;
            }

            return employee;
        }

        var admin = Make(Role.Admin, "perf.admin@perf.local", null);
        var managerList = Enumerable.Range(0, managers).Select(i => Make(Role.Manager, i < 2 ? $"perf.manager{i + 1}@perf.local" : null, null)).ToList();
        var financeList = Enumerable.Range(0, finance).Select(i => Make(Role.Finance, i < 2 ? $"perf.finance{i + 1}@perf.local" : null, null)).ToList();
        await db.SaveChangesAsync();

        var leadList = Enumerable.Range(0, leads).Select(i => Make(Role.TeamLead, i == 0 ? "perf.lead@perf.local" : null, managerList[i % managers].Id)).ToList();
        await db.SaveChangesAsync();

        var qaList = Enumerable.Range(0, qa).Select(i => Make(Role.QA, null, leadList[i % leads].Id)).ToList();
        var rest = n - 1 - managers - finance - leads - qa;
        var staff = Enumerable.Range(0, rest).Select(i => Make(Role.Employee, i == 0 ? "perf.employee@perf.local" : null, leadList[i % leads].Id)).ToList();
        await db.SaveChangesAsync();

        // A pay rise mid-history for some hourly and daily staff exercises rate-history lookups.
        foreach (var e in staff.Concat(qaList).Where(e => e.PayMode != PayMode.Monthly && _random.NextDouble() < 0.3))
        {
            var bump = 1.08m;
            e.ChangePay(e.PayMode, null, e.DailyRate * bump is { } d ? Math.Round(d, 2) : null, e.HourlyRate * bump is { } h ? Math.Round(h, 2) : null,
                new DateOnly(2025, 4, 1), Today, admin.Id, new DateTime(2025, 3, 20, 10, 0, 0, DateTimeKind.Utc));
        }

        await db.SaveChangesAsync();

        _managers.AddRange(managerList.Select(m => m.Id));
        _leads.AddRange(leadList.Select(l => l.Id));
        _qa.AddRange(qaList.Select(q => q.Id));
        _deliverers.AddRange(staff.Select(s => s.Id));

        int[] warranty = [30, 45, 60, 45, 30, 90, 45, 60];
        for (var p = 0; p < warranty.Length; p++)
        {
            var project = new Project($"Perf Project {p + 1}", warranty[p]);
            db.Projects.Add(project);
            var modules = Enumerable.Range(1, 6).Select(m => new Module(project, $"Module {p + 1}.{m}")).ToList();
            db.Modules.AddRange(modules);
            await db.SaveChangesAsync();
            _modules.AddRange(modules.Select(m => (m.Id, warranty[p])));
        }
    }

    // ---------------------------------------------------------------- tasks, time logs, bugs

    private sealed class TaskModel
    {
        public int Id;
        public int ModuleId;
        public int WarrantyDays;
        public DateOnly Start;
        public DateOnly? Completion;
        public DateOnly Committed;
        public List<int> Assignees = [];
        public WorkTaskStatus Status;
        public List<BugModel> Bugs = [];
        public List<string> History = [];
    }

    private sealed class BugModel
    {
        public int Id;
        public TaskModel Task = null!;
        public DateOnly Found;
        public DateOnly? FixDone;
        public DateOnly? FirstLog;
        public int? FirstLogBy;
        public bool TakesWork;
        public BugStatus Status;
    }

    private sealed record AuditRow(DateTime At, int? Actor, string EntityType, string EntityId, string Action, string Json);

    private async Task SeedWorkAsync()
    {
        BuildTasks();
        BuildBugs();
        BuildRegularLogs();
        BuildCorrectiveLogs();
        BuildLifecycle();

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        await BulkAsync(connection, "WorkTasks", "Id, ModuleId, Title, Status, CommittedDate, ActualCompletionDate, ReopenCount, Version",
            _tasks.Select(t => $"({t.Id},{t.ModuleId},{S($"Perf task {t.Id}: deliver feature slice")},{S(t.Status.ToString())},{D(t.Committed)},{D(t.Completion)},{t.Bugs.Count},0)"));
        await BulkAsync(connection, "TaskAssignments", "TaskId, EmployeeId, IsOriginalDeliverer",
            _tasks.SelectMany(t => t.Assignees.Select(a => $"({t.Id},{a},1)")));
        await BulkAsync(connection, "Bugs",
            "Id, TaskId, Severity, Description, FoundDate, ReportedByEmployeeId, WithinWarranty, RootCause, CausedByOriginalDelivery, ReviewedByEmployeeId, ReviewJustification, ReviewedAtUtc, Status, CreatedAtUtc, Version",
            _bugs.Select(BugRow));
        await BulkAsync(connection, "TimeLogs",
            "TaskId, EmployeeId, WorkDate, Hours, WorkType, BugId, ApprovalStatus, ApprovedByEmployeeId, ApprovedAtUtc, Note, CreatedAtUtc, Version",
            _logRows);
        await BulkAsync(connection, "TaskStateHistory", "TaskId, FromStatus, ToStatus, ChangedByEmployeeId, ChangedAtUtc, Reason",
            _tasks.SelectMany(t => t.History));
    }

    private void BuildTasks()
    {
        var months = Months(FirstMonth, CurrentMonth);
        for (var i = 0; i < _scale.Tasks; i++)
        {
            var month = months[i * months.Count / _scale.Tasks];
            var lastStartDay = month == CurrentMonth ? Today.Day - 2 : 28;
            var start = new DateOnly(month.Year, month.Month, _random.Next(1, lastStartDay + 1));
            var (moduleId, warranty) = _modules[_random.Next(_modules.Count)];
            var duration = _random.Next(2, 16);
            var task = new TaskModel
            {
                Id = i + 1,
                ModuleId = moduleId,
                WarrantyDays = warranty,
                Start = start,
                Committed = start.AddDays(duration + _random.Next(-3, 6)),
            };

            var pool = _random.NextDouble() < 0.1 ? _leads : _deliverers;
            task.Assignees.Add(pool[_random.Next(pool.Count)]);
            if (_random.NextDouble() < 0.25)
            {
                var second = _deliverers[_random.Next(_deliverers.Count)];
                if (!task.Assignees.Contains(second))
                {
                    task.Assignees.Add(second);
                }
            }

            var completion = start.AddDays(duration);
            if (completion < Today.AddDays(-1))
            {
                task.Completion = completion;
                task.Status = WorkTaskStatus.Completed;
            }
            else
            {
                task.Status = _random.NextDouble() < 0.3 ? WorkTaskStatus.Assigned : WorkTaskStatus.InProgress;
            }

            _tasks.Add(task);
        }
    }

    private void BuildBugs()
    {
        var candidates = _tasks.Where(t => t.Completion is { } c && c < Today.AddDays(-2)).ToList();
        var order = candidates.OrderBy(_ => _random.Next()).ToList();
        var nextId = 1;
        foreach (var task in order)
        {
            if (nextId > _scale.Bugs)
            {
                break;
            }

            var count = _random.NextDouble() < 0.2 ? 2 : 1;
            var cursor = task.Completion!.Value;
            for (var b = 0; b < count && nextId <= _scale.Bugs; b++)
            {
                var latest = Min(task.Completion.Value.AddDays(task.WarrantyDays), Today);
                if (cursor.AddDays(1) > latest)
                {
                    break;
                }

                var found = cursor.AddDays(_random.Next(1, latest.DayNumber - cursor.DayNumber + 1));
                var fixDone = found.AddDays(_random.Next(1, 7));
                var bug = new BugModel
                {
                    Id = nextId++,
                    Task = task,
                    Found = found,
                    FixDone = fixDone < Today ? fixDone : null,
                };
                bug.TakesWork = bug.FixDone is not null || _random.NextDouble() < 0.8;
                task.Bugs.Add(bug);
                _bugs.Add(bug);
                if (bug.FixDone is null)
                {
                    break;
                }

                cursor = bug.FixDone.Value;
            }
        }
    }

    private string BugRow(BugModel bug)
    {
        var task = bug.Task;
        var within = WarrantyPolicy.IsWithinWarranty(task.Completion!.Value, task.WarrantyDays, bug.Found);
        var severity = (BugSeverity)_random.Next(4);
        var reporter = _random.NextDouble() < 0.8 ? _qa[_random.Next(_qa.Count)] : _leads[_random.Next(_leads.Count)];
        string rootCause = "NULL", caused = "NULL", reviewer = "NULL", justification = "NULL", reviewedAt = "NULL";
        if (bug.Found < Today.AddDays(-5) && _random.NextDouble() < 0.9)
        {
            var cause = RootCauses[_random.Next(RootCauses.Length)];
            rootCause = S(cause.ToString());
            caused = _random.NextDouble() < (cause == RootCause.OwnDefect ? 0.9 : 0.3) ? "1" : "0";
            reviewer = I(_managers[_random.Next(_managers.Count)]);
            justification = S("Reviewed against the original acceptance criteria");
            reviewedAt = T(At(bug.Found.AddDays(_random.Next(1, 4)), 15));
        }

        var created = At(bug.Found, 10);
        _audit.Add(new AuditRow(created, reporter, AuditEntities.Bug, Id(bug.Id), "Reported", $"{{\"taskId\":{task.Id},\"severity\":\"{severity}\"}}"));
        if (reviewer != "NULL")
        {
            _audit.Add(new AuditRow(At(bug.Found.AddDays(1), 15), int.Parse(reviewer, CultureInfo.InvariantCulture), AuditEntities.Bug, Id(bug.Id), "Reviewed", $"{{\"rootCause\":{rootCause.Replace('\'', '"')}}}"));
        }

        return $"({bug.Id},{task.Id},{S(severity.ToString())},{S($"Perf bug {bug.Id}: regression found after delivery")},{D(bug.Found)},{reporter},{(within ? 1 : 0)},{rootCause},{caused},{reviewer},{justification},{reviewedAt},{S(bug.Status.ToString())},{T(created)},0)";
    }

    private void BuildRegularLogs()
    {
        for (var j = 0; j < _scale.RegularLogs; j++)
        {
            var task = _tasks[j % _tasks.Count];
            var end = task.Completion ?? Today;
            var employee = task.Assignees[_random.Next(task.Assignees.Count)];
            AddLog(task, null, employee, task.Start, end);
        }
    }

    private void BuildCorrectiveLogs()
    {
        var worked = _bugs.Where(b => b.TakesWork).ToList();
        if (worked.Count == 0)
        {
            return;
        }

        for (var j = 0; j < _scale.CorrectiveLogs; j++)
        {
            var bug = worked[j % worked.Count];
            var employee = bug.Task.Assignees[_random.Next(bug.Task.Assignees.Count)];
            var date = AddLog(bug.Task, bug, employee, bug.Found, bug.FixDone ?? Today);
            if (bug.FirstLog is null || date < bug.FirstLog)
            {
                bug.FirstLog = date;
                bug.FirstLogBy = employee;
            }
        }
    }

    private DateOnly AddLog(TaskModel task, BugModel? bug, int employee, DateOnly from, DateOnly to)
    {
        var span = Math.Max(0, to.DayNumber - from.DayNumber);
        var hours = _random.Next(2, 17) / 2m;
        var date = from.AddDays(_random.Next(span + 1));
        for (var attempt = 0; attempt < 6 && _dayHours.GetValueOrDefault((employee, date)) + hours > 10m; attempt++)
        {
            date = from.AddDays(_random.Next(span + 1));
        }

        var already = _dayHours.GetValueOrDefault((employee, date));
        hours = Math.Max(0.5m, Math.Min(hours, 24m - already));
        _dayHours[(employee, date)] = already + hours;

        var status = date <= Today.AddDays(-4) ? ApprovalStatus.Approved : _random.NextDouble() switch
        {
            < 0.7 => ApprovalStatus.Approved,
            < 0.95 => ApprovalStatus.Pending,
            _ => ApprovalStatus.Rejected,
        };
        var approver = _leads[_random.Next(_leads.Count)];
        if (approver == employee)
        {
            approver = _managers[0];
        }

        var created = At(date, 18);
        var decided = status == ApprovalStatus.Pending ? (DateTime?)null : At(Min(date.AddDays(1), Today), 11);
        var workType = bug is null ? WorkType.Regular : WorkType.Corrective;
        _logRows.Add(
            $"({task.Id},{employee},{D(date)},{N(hours)},{S(workType.ToString())},{I(bug?.Id)},{S(status.ToString())},{(decided is null ? "NULL" : I(approver))},{T(decided)},NULL,{T(created)},0)");
        var logKey = Id(_logRows.Count); // TimeLogs is empty before the bulk insert, so ids follow insertion order
        _audit.Add(new AuditRow(created, employee, AuditEntities.TimeLog, logKey, "Logged", $"{{\"taskId\":{task.Id},\"hours\":{N(hours)},\"workType\":\"{workType}\"}}"));
        if (decided is { } at)
        {
            _audit.Add(new AuditRow(at, approver, AuditEntities.TimeLog, logKey, status.ToString(), "{}"));
        }

        return date;
    }

    private void BuildLifecycle()
    {
        foreach (var task in _tasks)
        {
            var actor = task.Assignees[0];
            _audit.Add(new AuditRow(At(task.Start, 8), _leads[_random.Next(_leads.Count)], AuditEntities.Task, Id(task.Id), "Created", $"{{\"moduleId\":{task.ModuleId}}}"));
            if (task.Status == WorkTaskStatus.Assigned)
            {
                continue;
            }

            Hist(task, WorkTaskStatus.Assigned, WorkTaskStatus.InProgress, actor, At(task.Start, 9), "Started");
            if (task.Completion is not { } completion)
            {
                continue;
            }

            Hist(task, WorkTaskStatus.InProgress, WorkTaskStatus.Completed, actor, At(completion, 17), "Delivered");
            var lastFix = completion;
            var allVerified = true;
            foreach (var bug in task.Bugs)
            {
                Hist(task, WorkTaskStatus.Completed, WorkTaskStatus.Reopened, null, At(bug.Found, 10), $"Bug reported: Perf bug {bug.Id}");
                task.Status = WorkTaskStatus.Reopened;
                bug.Status = BugStatus.Open;
                if (bug.FirstLog is { } first)
                {
                    Hist(task, WorkTaskStatus.Reopened, WorkTaskStatus.UnderFix, bug.FirstLogBy, At(first, 11), "First corrective hours logged");
                    task.Status = WorkTaskStatus.UnderFix;
                }

                if (bug.FixDone is { } fixedOn)
                {
                    Hist(task, WorkTaskStatus.UnderFix, WorkTaskStatus.Completed, actor, At(fixedOn, 16), "Fix delivered");
                    task.Status = WorkTaskStatus.Completed;
                    lastFix = fixedOn;
                    var verified = fixedOn.AddDays(_random.Next(1, 4));
                    bug.Status = verified <= Today ? (verified < Today.AddDays(-30) && _random.NextDouble() < 0.5 ? BugStatus.Closed : BugStatus.Verified) : BugStatus.Fixed;
                    allVerified &= bug.Status is BugStatus.Verified or BugStatus.Closed;
                }
            }

            var closeOn = Max(completion.AddDays(task.WarrantyDays + 1), lastFix.AddDays(1));
            if (task.Status == WorkTaskStatus.Completed && allVerified && closeOn <= Today)
            {
                Hist(task, WorkTaskStatus.Completed, WorkTaskStatus.Closed, null, At(closeOn, 2), $"Warranty window of {task.WarrantyDays} days ended");
                task.Status = WorkTaskStatus.Closed;
            }
        }
    }

    private void Hist(TaskModel task, WorkTaskStatus from, WorkTaskStatus to, int? actor, DateTime at, string reason)
    {
        task.History.Add($"({task.Id},{S(from.ToString())},{S(to.ToString())},{I(actor)},{T(at)},{S(reason)})");
        _audit.Add(new AuditRow(at, actor, AuditEntities.Task, Id(task.Id), "StatusChanged", $"{{\"from\":\"{from}\",\"to\":\"{to}\"}}"));
    }

    // ---------------------------------------------------------------- audit and notifications

    private async Task SeedAuditAndNotificationsAsync()
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // In time order, as they would have been written; the BEFORE INSERT trigger chains each row's hash.
        await BulkAsync(connection, "AuditEvents", "OccurredAtUtc, ActorEmployeeId, EntityType, EntityId, Action, DetailsJson",
            _audit.OrderBy(a => a.At).Select(a => $"({T(a.At)},{I(a.Actor)},{S(a.EntityType)},{S(a.EntityId)},{S(a.Action)},{S(a.Json)})"));

        NotificationType[] types =
        [
            NotificationType.BugReported, NotificationType.TimeLogRejected, NotificationType.DeductionNotified,
            NotificationType.PayslipPublished, NotificationType.TimeLogsAwaitingApproval, NotificationType.DeductionApproved,
        ];
        var span = Today.DayNumber - FirstMonth.FirstDay.DayNumber;
        var rows = new List<string>();
        foreach (var employee in _employees)
        {
            for (var k = 0; k < _scale.NotificationsPerEmployee; k++)
            {
                var type = types[_random.Next(types.Length)];
                var created = At(FirstMonth.FirstDay.AddDays(_random.Next(span + 1)), _random.Next(7, 19));
                var read = created < NowUtc.AddDays(-14) && _random.NextDouble() < 0.85 ? created.AddHours(_random.Next(1, 48)) : (DateTime?)null;
                rows.Add($"({employee.Id},{S(type.ToString())},{S($"{type} notice")},{S("Perf seed notification body")},{S("/tasks")},NULL,NULL,{T(created)},{T(read)})");
            }
        }

        await BulkAsync(connection, "Notifications", "RecipientEmployeeId, Type, Title, Body, Link, EntityType, EntityId, CreatedAtUtc, ReadAtUtc", rows);
    }

    // ---------------------------------------------------------------- deductions

    private async Task DraftDeductionsAsync()
    {
        foreach (var chunk in _bugs.Select(b => b.Id).Chunk(100))
        {
            await using var scope = _api.Services.CreateAsyncScope();
            var ledger = scope.ServiceProvider.GetRequiredService<DeductionLedger>();
            await ledger.SyncBugsAsync(chunk, "perf seed", CancellationToken.None);
        }
    }

    private async Task MoveDeductionsAsync()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        var lines = await db.Deductions.Where(d => d.Status == DeductionStatus.Draft).OrderBy(d => d.Id).ToListAsync();
        var current = CurrentMonth.ToString();
        foreach (var line in lines)
        {
            var manager = _managers[_random.Next(_managers.Count)];
            if (manager == line.EmployeeId)
            {
                manager = _managers.First(m => m != line.EmployeeId);
            }

            var notifiedAt = At(line.WorkDate.AddDays(2), 10);
            var windowEnds = notifiedAt.AddDays(5);
            var roll = _random.NextDouble();
            var past = string.CompareOrdinal(line.ApplyMonth, current) < 0;
            var due = line.ApplyMonth == current;
            if (past || (due && roll < 0.55))
            {
                line.Notify(notifiedAt, windowEnds);
                if (past && roll < 0.03)
                {
                    line.CancelByDecision("Waived by manager", windowEnds.AddHours(1));
                    audit.Record(AuditEntities.Deduction, Id(line.Id), "Cancelled", new { line.CancellationReason });
                }
                else
                {
                    line.Approve(manager, windowEnds.AddHours(1));
                    audit.Record(AuditEntities.Deduction, Id(line.Id), "Approved", new { ApprovedBy = manager });
                }
            }
            else if (due && roll < 0.85)
            {
                line.Notify(NowUtc.AddDays(-2), NowUtc.AddDays(5));
                audit.Record(AuditEntities.Deduction, Id(line.Id), "Notified", new { line.DisputeWindowEndsAtUtc });
                if (roll >= 0.75)
                {
                    line.RaiseDispute(line.EmployeeId, "I was not the original developer of this part", NowUtc.AddDays(-1));
                    audit.Record(AuditEntities.Deduction, Id(line.Id), "Disputed", new { line.EmployeeId });
                }
            }
        }

        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- payroll

    private async Task RunPastPayrollsAsync(List<TimeSpan> generateTimings)
    {
        var finance1 = await ClientAsync(_users["perf.finance1"]);
        var finance2 = await ClientAsync(_users["perf.finance2"]);
        var manager = await ClientAsync(_users["perf.manager1"]);
        foreach (var month in Months(FirstMonth, new YearMonth(2026, 2)))
        {
            var stopwatch = Stopwatch.StartNew();
            var run = await StepAsync(finance1, "/api/v1/payroll/runs", new GeneratePayrollRunRequest(month.ToString()));
            generateTimings.Add(stopwatch.Elapsed);
            run = await StepAsync(finance2, $"/api/v1/payroll/runs/{run.Id}/review", new PayrollRunStepRequest(run.Version));
            run = await StepAsync(manager, $"/api/v1/payroll/runs/{run.Id}/approve", new PayrollRunStepRequest(run.Version));
            run = await StepAsync(finance1, $"/api/v1/payroll/runs/{run.Id}/finalize", new PayrollRunStepRequest(run.Version));
            await StepAsync(finance1, $"/api/v1/payroll/runs/{run.Id}/lock", new PayrollRunStepRequest(run.Version));
        }
    }

    private static async Task<(int Id, int Version)> StepAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, ApiFactory.Json);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {url} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        var detail = await response.Content.ReadFromJsonAsync<PayrollRunDetailDto>(ApiFactory.Json);
        return (detail!.Run.Id, detail.Run.Version);
    }

    public async Task<HttpClient> ClientAsync(string email)
    {
        var client = _api.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, TestData.Password), ApiFactory.Json);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(ApiFactory.Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    // ---------------------------------------------------------------- summary

    private async Task<PerfSeedResult> SummariseAsync(List<TimeSpan> generateTimings)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        string[] tables =
        [
            "Employees", "EmployeeRateHistory", "Projects", "Modules", "WorkTasks", "TaskAssignments", "TaskStateHistory", "TimeLogs", "Bugs",
            "Deductions", "DeductionDisputes", "PayrollRuns", "PayrollRunEmployees", "PayrollRunItems", "AuditEvents", "Notifications",
        ];
        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            counts[table] = await ScalarAsync(connection, $"SELECT COUNT(*) FROM `{table}`");
        }

        var busy = (int)await ScalarAsync(connection, "SELECT EmployeeId FROM Deductions GROUP BY EmployeeId ORDER BY COUNT(*) DESC, EmployeeId LIMIT 1");
        var task = (int)await ScalarAsync(connection, "SELECT TaskId FROM Bugs GROUP BY TaskId ORDER BY COUNT(*) DESC, TaskId LIMIT 1");
        var run = (int)await ScalarAsync(connection, "SELECT Id FROM PayrollRuns WHERE Status = 'Locked' ORDER BY PeriodMonth DESC LIMIT 1");
        var payslip = await ScalarAsync(connection, $"SELECT Id FROM PayrollRunEmployees WHERE PayrollRunId = {run} AND EmployeeId = {busy}");
        return new PerfSeedResult(_users, busy, task, run, payslip, counts, _phases, generateTimings);
    }

    // ---------------------------------------------------------------- SQL helpers

    private static async Task BulkAsync(MySqlConnection connection, string table, string columns, IEnumerable<string> rows)
    {
        foreach (var batch in rows.Chunk(1000))
        {
            var sql = new StringBuilder($"INSERT INTO `{table}` ({columns}) VALUES ");
            sql.AppendJoin(',', batch);
            await using var command = new MySqlCommand(sql.ToString(), connection) { CommandTimeout = 600 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> ScalarAsync(MySqlConnection connection, string sql)
    {
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public static List<YearMonth> Months(YearMonth from, YearMonth to)
    {
        var months = new List<YearMonth>();
        for (var m = from; m <= to; m = m.Next())
        {
            months.Add(m);
        }

        return months;
    }

    private static DateTime At(DateOnly date, int hourUtc) => date.ToDateTime(new TimeOnly(hourUtc, 0), DateTimeKind.Utc);

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string S(string value) => "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string I(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "NULL";

    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string D(DateOnly? value) => value is { } d ? $"'{d:yyyy-MM-dd}'" : "NULL";

    private static string T(DateTime? value) => value is { } t ? $"'{t.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}'" : "NULL";
}
