namespace ReworkGuard.Api.Authorization;

/// <summary>Role names and role groups used in [Authorize(Roles = ...)] attributes.</summary>
public static class Roles
{
    public const string Employee = nameof(Domain.Enums.Role.Employee);
    public const string QA = nameof(Domain.Enums.Role.QA);
    public const string TeamLead = nameof(Domain.Enums.Role.TeamLead);
    public const string Manager = nameof(Domain.Enums.Role.Manager);
    public const string Finance = nameof(Domain.Enums.Role.Finance);
    public const string Admin = nameof(Domain.Enums.Role.Admin);

    public const string Everyone = $"{Employee},{QA},{TeamLead},{Manager},{Finance},{Admin}";
    public const string Delivery = $"{Employee},{QA},{TeamLead},{Manager}";
    public const string TaskPlanners = $"{TeamLead},{Manager}";
    public const string BugReporters = $"{QA},{TeamLead},{Manager}";
    public const string BugVerifiers = $"{QA},{TeamLead},{Manager}";
    public const string Approvers = $"{TeamLead},{Manager}";
    public const string Reviewers = Manager;
    public const string People = $"{TeamLead},{Manager},{Finance},{Admin}";
    public const string Auditors = $"{Manager},{Admin}";
    public const string DeductionDeciders = Manager;
    public const string Administrators = Admin;
    public const string PayViewers = $"{Finance},{Admin}";
    public const string PolicyViewers = $"{Manager},{Finance},{Admin}";
    public const string PayrollOperators = Finance;
    public const string PayrollApprovers = Manager;
    public const string PayrollViewers = $"{Manager},{Finance},{Admin}";
    public const string ReportAggregateViewers = $"{TeamLead},{Manager},{Finance},{Admin}";
    public const string ChangeRequestCreators = $"{QA},{TeamLead},{Manager}";
    public const string ChangeRequestViewers = $"{QA},{TeamLead},{Manager},{Finance},{Admin}";
}
