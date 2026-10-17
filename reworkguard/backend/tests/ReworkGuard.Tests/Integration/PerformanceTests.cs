using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using MySqlConnector;
using ReworkGuard.Application.Payroll;
using ReworkGuard.Tests.Support;
using ReworkGuard.Tests.Support.Perf;
using Xunit.Abstractions;

namespace ReworkGuard.Tests.Integration;

/// <summary>A test that only runs when RG_PERF=1 (it seeds a large data set and takes minutes).</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RG_PERF") != "1")
        {
            Skip = "Performance test: set RG_PERF=1 to run (RG_PERF_SCALE=1 for the full volume).";
        }
    }
}

/// <summary>
/// Seeds 24 months of history (RG_PERF_SCALE of the full 200 employees / 5,000 tasks / 20,000 time logs; 0.25 by
/// default), times the key endpoints through the real API and asserts every p95 stays inside the 2 s budget. Writes a
/// Markdown report with the timings, the heaviest statements from performance_schema and their EXPLAIN ANALYZE plans
/// to RG_PERF_OUT when set.
/// </summary>
[Collection(MySqlTestGroup.Name)]
[Trait("Category", "Performance")]
public sealed class PerformanceTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    private readonly MySqlContainerFixture _mysql;
    private readonly ITestOutputHelper _output;

    public PerformanceTests(MySqlContainerFixture mysql, ITestOutputHelper output)
    {
        _mysql = mysql;
        _output = output;
    }

    private sealed record Case(string Name, HttpClient Client, Func<int, string> Url, int Iterations);

    private sealed record Timing(string Name, int Samples, double P50, double P95, double Max, long Bytes);

    [PerfFact]
    public async Task Key_endpoints_respond_within_budget_on_volume_data()
    {
        var scale = PerfScale.FromEnvironment();
        var iterations = EnvInt("RG_PERF_ITERATIONS", 20);
        var generateRuns = EnvInt("RG_PERF_GENERATE_RUNS", 3);
        var connectionString = await _mysql.CreateMigratedDatabaseAsync();
        var database = new MySqlConnectionStringBuilder(connectionString).Database;
        var clock = new TestClock { UtcNow = PerfSeeder.NowUtc };
        await using var api = new ApiFactory(connectionString, clock);

        var seeder = new PerfSeeder(connectionString, api, scale, _output.WriteLine);
        var seed = await seeder.SeedAsync();

        var admin = await seeder.ClientAsync(seed.Users["perf.admin"]);
        var manager = await seeder.ClientAsync(seed.Users["perf.manager1"]);
        var finance = await seeder.ClientAsync(seed.Users["perf.finance1"]);
        var lead = await seeder.ClientAsync(seed.Users["perf.lead"]);
        var employee = await seeder.ClientAsync(seed.Users["perf.employee"]);
        var range = "from=2024-04&to=2026-03";

        await ResetDigestsAsync(connectionString);

        // Payroll generate for the open month: each call supersedes the previous preview and recomputes everything.
        var generate = new List<double>();
        var generatedRunId = 0;
        for (var i = 0; i < generateRuns + 1; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await finance.PostAsJsonAsync("/api/v1/payroll/runs", new GeneratePayrollRunRequest(PerfSeeder.CurrentMonth.ToString()), ApiFactory.Json);
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            await response.ShouldHaveStatusAsync(System.Net.HttpStatusCode.OK);
            generatedRunId = (await response.Content.ReadFromJsonAsync<PayrollRunDetailDto>(ApiFactory.Json))!.Run.Id;
            if (i > 0)
            {
                generate.Add(elapsed); // the first call is the warm-up
            }
        }

        var cases = new List<Case>
        {
            new("GET /tasks (page 1, 50)", manager, _ => "/api/v1/tasks?page=1&pageSize=50", iterations),
            new("GET /tasks?status=Closed (deep pages)", manager, i => $"/api/v1/tasks?status=Closed&page={1 + (i % 20)}&pageSize=50", iterations),
            new("GET /tasks?assigneeId (board, 100)", lead, _ => $"/api/v1/tasks?assigneeId={seed.BusyEmployeeId}&pageSize=100", iterations),
            new("GET /tasks (employee board)", employee, _ => "/api/v1/tasks?pageSize=100", iterations),
            new("GET /tasks/{id} (detail)", manager, _ => $"/api/v1/tasks/{seed.TaskWithBugsId}", iterations),
            new("GET /deductions (manager, page 1)", manager, _ => "/api/v1/deductions?pageSize=50", iterations),
            new("GET /deductions?status&applyMonth", manager, _ => "/api/v1/deductions?status=Applied&applyMonth=2025-06&pageSize=50", iterations),
            new("GET /deductions?employeeId", manager, _ => $"/api/v1/deductions?employeeId={seed.BusyEmployeeId}&pageSize=50", iterations),
            new("GET /deductions?openDispute", manager, _ => "/api/v1/deductions?openDispute=true&pageSize=50", iterations),
            new("GET /deductions/preview", manager, _ => $"/api/v1/deductions/preview?employeeId={seed.BusyEmployeeId}&periodMonth={PerfSeeder.CurrentMonth}", iterations),
            new("GET /payroll/runs/{id} (generated)", finance, _ => $"/api/v1/payroll/runs/{generatedRunId}", iterations),
            new("GET /payroll/runs/{id} (locked)", finance, _ => $"/api/v1/payroll/runs/{seed.LatestLockedRunId}", iterations),
            new("GET /payroll/runs/{id}/employees/{e}", finance, _ => $"/api/v1/payroll/runs/{seed.LatestLockedRunId}/employees/{seed.BusyEmployeeId}", iterations),
            new("GET /payroll/runs", finance, _ => "/api/v1/payroll/runs", iterations),
            new("GET /payslips (finance, month)", finance, _ => "/api/v1/payslips?periodMonth=2026-02", iterations),
            new("GET /payslips (employee, own)", employee, _ => "/api/v1/payslips", iterations),
            new("GET /payslips/{id}/pdf", finance, _ => $"/api/v1/payslips/{seed.PayslipId}/pdf", iterations),
            new("GET /reports/rework-summary (24 months)", manager, _ => $"/api/v1/reports/rework-summary?{range}", iterations),
            new("GET /reports/by-employee (24 months)", manager, _ => $"/api/v1/reports/by-employee?{range}", iterations),
            new("GET /reports/by-module (24 months)", manager, _ => $"/api/v1/reports/by-module?{range}", iterations),
            new("GET /reports/payroll-register", finance, _ => "/api/v1/reports/payroll-register?periodMonth=2026-02", iterations),
            new("GET /reports/payroll-register xlsx", finance, _ => "/api/v1/reports/payroll-register?periodMonth=2026-02&format=xlsx", iterations),
            new("GET /reports/rework-summary xlsx", manager, _ => $"/api/v1/reports/rework-summary?{range}&format=xlsx", iterations),
            new("GET /audit-events (page 1)", admin, _ => "/api/v1/audit-events?pageSize=50", iterations),
            new("GET /audit-events?entityType=Deduction", admin, i => $"/api/v1/audit-events?entityType=Deduction&page={1 + (i % 5)}&pageSize=50", iterations),
            new("GET /audit/verify (full chain)", admin, _ => "/api/v1/audit/verify", iterations),
            new("GET /notifications (inbox)", employee, _ => "/api/v1/notifications?pageSize=20", iterations),
            new("GET /notifications/unread-count", employee, _ => "/api/v1/notifications/unread-count", iterations),
        };

        var timings = new List<Timing> { Summarise($"POST /payroll/runs (generate {PerfSeeder.CurrentMonth})", generate, 0) };
        foreach (var c in cases)
        {
            timings.Add(await MeasureAsync(c));
        }

        var digests = await CollectDigestsAsync(connectionString, database);
        var report = Report(scale, seed, timings, digests);
        _output.WriteLine(report);
        if (Environment.GetEnvironmentVariable("RG_PERF_OUT") is { Length: > 0 } path)
        {
            await File.WriteAllTextAsync(path, report);
        }

        timings.Where(t => t.P95 > Budget.TotalMilliseconds).Should().BeEmpty("every key endpoint must answer within {0} at p95", Budget);
    }

    private static async Task<Timing> MeasureAsync(Case c)
    {
        long bytes = 0;
        for (var warm = 0; warm < 2; warm++)
        {
            using var response = await c.Client.GetAsync(c.Url(warm));
            await response.ShouldHaveStatusAsync(System.Net.HttpStatusCode.OK);
        }

        var samples = new List<double>(c.Iterations);
        for (var i = 0; i < c.Iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await c.Client.GetAsync(c.Url(i));
            var body = await response.Content.ReadAsByteArrayAsync();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{c.Name} returned {(int)response.StatusCode}: {Encoding.UTF8.GetString(body)}");
            }

            bytes = body.Length;
        }

        return Summarise(c.Name, samples, bytes);
    }

    private static Timing Summarise(string name, List<double> samples, long bytes)
    {
        var sorted = samples.Order().ToList();
        double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
        return new Timing(name, sorted.Count, Percentile(0.50), Percentile(0.95), sorted[^1], bytes);
    }

    private sealed record Digest(string Sample, long Calls, double TotalMs, double AvgMs, long RowsExamined, long RowsSent, long NoIndexUsed, string Plan);

    private static async Task ResetDigestsAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand("TRUNCATE TABLE performance_schema.events_statements_summary_by_digest", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<Digest>> CollectDigestsAsync(string connectionString, string database)
    {
        var digests = new List<Digest>();
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = new MySqlCommand(
                         """
                         SELECT QUERY_SAMPLE_TEXT, COUNT_STAR, SUM_TIMER_WAIT / 1e9, AVG_TIMER_WAIT / 1e9, SUM_ROWS_EXAMINED, SUM_ROWS_SENT, SUM_NO_INDEX_USED
                         FROM performance_schema.events_statements_summary_by_digest
                         WHERE SCHEMA_NAME = @db AND QUERY_SAMPLE_TEXT LIKE 'SELECT%'
                         ORDER BY SUM_TIMER_WAIT DESC LIMIT 12
                         """, connection))
        {
            command.Parameters.AddWithValue("@db", database);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                digests.Add(new Digest(reader.GetString(0), reader.GetInt64(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), string.Empty));
            }
        }

        for (var i = 0; i < digests.Count; i++)
        {
            string plan;
            try
            {
                await using var explain = new MySqlCommand("EXPLAIN ANALYZE " + digests[i].Sample, connection) { CommandTimeout = 120 };
                plan = Convert.ToString(await explain.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (MySqlException ex)
            {
                plan = $"(EXPLAIN failed: {ex.Message})";
            }

            digests[i] = digests[i] with { Plan = plan };
        }

        // Month- and quarter-sized versions of the payroll and report scans, which the 24-month report ranges hide
        // (over the whole history every row qualifies and a table scan is the right plan).
        string[] targeted =
        [
            "SELECT `EmployeeId`, `WorkDate`, SUM(`Hours`) FROM `TimeLogs` WHERE `ApprovalStatus` = 'Approved' AND `WorkDate` >= '2026-02-01' AND `WorkDate` <= '2026-02-28' GROUP BY `EmployeeId`, `WorkDate`",
            "SELECT `Id`, `EmployeeId`, `WorkDate`, `Hours` FROM `TimeLogs` WHERE `ApprovalStatus` = 'Pending' ORDER BY `WorkDate` LIMIT 50",
            "SELECT YEAR(`FoundDate`), MONTH(`FoundDate`), COUNT(*) FROM `Bugs` WHERE `FoundDate` >= '2026-01-01' AND `FoundDate` <= '2026-03-31' GROUP BY 1, 2",
            "SELECT w.`ModuleId`, COUNT(*) FROM `TaskStateHistory` t JOIN `WorkTasks` w ON t.`TaskId` = w.`Id` WHERE t.`ToStatus` = 'Reopened' AND t.`ChangedAtUtc` >= '2025-12-31 18:30:00' AND t.`ChangedAtUtc` < '2026-03-31 18:30:00' GROUP BY w.`ModuleId`",
        ];
        foreach (var sql in targeted)
        {
            await using var explain = new MySqlCommand("EXPLAIN ANALYZE " + sql, connection) { CommandTimeout = 120 };
            var plan = Convert.ToString(await explain.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
            digests.Add(new Digest(sql, 0, 0, 0, 0, 0, 0, plan));
        }

        return digests;
    }

    private static string Report(PerfScale scale, PerfSeedResult seed, List<Timing> timings, List<Digest> digests)
    {
        var s = new StringBuilder();
        string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        s.AppendLine(CultureInfo.InvariantCulture, $"## Run {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, scale {scale}");
        s.AppendLine(CultureInfo.InvariantCulture, $"Machine: {Environment.ProcessorCount} CPUs, load average {LoadAverage()}");
        s.AppendLine();
        s.AppendLine("| Table | Rows |");
        s.AppendLine("|---|---:|");
        foreach (var (table, rows) in seed.RowCounts)
        {
            s.AppendLine(CultureInfo.InvariantCulture, $"| {table} | {rows:N0} |");
        }

        s.AppendLine();
        s.AppendLine("| Seed phase | Seconds |");
        s.AppendLine("|---|---:|");
        foreach (var (phase, elapsed) in seed.Phases)
        {
            s.AppendLine(CultureInfo.InvariantCulture, $"| {phase} | {F(elapsed.TotalSeconds)} |");
        }

        if (seed.PayrollGenerateTimings.Count > 0)
        {
            var g = seed.PayrollGenerateTimings.Select(t => t.TotalMilliseconds).Order().ToList();
            s.AppendLine();
            s.AppendLine(CultureInfo.InvariantCulture, $"Payroll generate during seeding ({g.Count} months, growing history): min {F(g[0])} ms, median {F(g[g.Count / 2])} ms, max {F(g[^1])} ms");
        }

        s.AppendLine();
        s.AppendLine("| Endpoint | n | p50 ms | p95 ms | max ms | bytes | 2 s budget |");
        s.AppendLine("|---|---:|---:|---:|---:|---:|---|");
        foreach (var t in timings)
        {
            s.AppendLine(CultureInfo.InvariantCulture, $"| {t.Name} | {t.Samples} | {F(t.P50)} | {F(t.P95)} | {F(t.Max)} | {t.Bytes:N0} | {(t.P95 <= Budget.TotalMilliseconds ? "pass" : "FAIL")} |");
        }

        s.AppendLine();
        s.AppendLine("### Heaviest SELECT statements during the measured run (performance_schema)");
        foreach (var d in digests)
        {
            s.AppendLine();
            s.AppendLine(d.Calls == 0 ? "- targeted query (month/quarter range)" : string.Create(CultureInfo.InvariantCulture, $"- calls {d.Calls}, total {F(d.TotalMs)} ms, avg {F(d.AvgMs)} ms, rows examined/call {d.RowsExamined / Math.Max(1, d.Calls):N0}, rows sent/call {d.RowsSent / Math.Max(1, d.Calls):N0}, no-index-used {d.NoIndexUsed}"));
            s.AppendLine();
            s.AppendLine("```sql");
            s.AppendLine(d.Sample);
            s.AppendLine("```");
            s.AppendLine("```");
            s.AppendLine(d.Plan.TrimEnd());
            s.AppendLine("```");
        }

        return s.ToString();
    }

    private static string LoadAverage()
    {
        try
        {
            return File.Exists("/proc/loadavg") ? string.Join(' ', File.ReadAllText("/proc/loadavg").Split(' ').Take(3)) : "n/a";
        }
        catch (IOException)
        {
            return "n/a";
        }
    }

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
}
