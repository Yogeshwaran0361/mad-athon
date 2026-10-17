using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Entities;
using ReworkGuard.Domain.Enums;

namespace ReworkGuard.Tests.Domain.Deductions;

public sealed class DeductionLifecycleTests
{
    private const int Owner = 10;
    private const int Other = 11;
    private const int Manager = 40;
    private static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd = Now.AddDays(3);
    private static readonly YearMonth April = YearMonth.Parse("2026-04");
    private static readonly DateOnly MarchWorkDate = new(2026, 3, 9);

    private static Deduction Draft(decimal hours = 4m, decimal rate = 300m, decimal share = 75m) =>
        Deduction.Create(Owner, bugId: 1, timeLogId: 7, MarchWorkDate, April, hours, rate, share, Now);

    private static Deduction Notified()
    {
        var line = Draft();
        line.Notify(Now, WindowEnd);
        return line;
    }

    private static Deduction Disputed()
    {
        var line = Notified();
        line.RaiseDispute(Owner, "I was on leave that week", Now.AddHours(1));
        return line;
    }

    private static Deduction Approved()
    {
        var line = Notified();
        line.Approve(Manager, WindowEnd.AddTicks(1));
        return line;
    }

    private static Deduction Applied()
    {
        var line = Approved();
        line.Lock(payrollRunId: 5);
        line.Apply(requireAcknowledgement: false, WindowEnd.AddDays(20));
        return line;
    }

    private static void ShouldFail(Action act, string code) =>
        act.Should().Throw<DomainException>().Which.Code.Should().Be(code);

    // ---------- Creation ----------

    [Fact]
    public void A_new_line_snapshots_hours_rate_share_and_the_rounded_amount()
    {
        var line = Draft(hours: 1m, rate: 100.01m, share: 50m);

        line.Status.Should().Be(DeductionStatus.Draft);
        line.Kind.Should().Be(DeductionKind.Standard);
        line.Amount.Should().Be(50.01m, "50.005 rounds away from zero");
        line.SourceMonth.Should().Be("2026-03");
        line.ApplyMonth.Should().Be("2026-04");
        line.RoutedFromLockedMonth.Should().BeFalse();
    }

    [Fact]
    public void A_line_routed_past_the_next_month_is_flagged()
    {
        var line = Deduction.Create(Owner, 1, 7, MarchWorkDate, YearMonth.Parse("2026-05"), 1m, 300m, 100m, Now);

        line.RoutedFromLockedMonth.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 300, 50, "deduction.invalid_hours")]
    [InlineData(1.005, 300, 50, "deduction.invalid_hours")]
    [InlineData(1, 300, 0, "deduction.invalid_share")]
    [InlineData(1, 300, 100.01, "deduction.invalid_share")]
    [InlineData(1, 0, 50, "deduction.invalid_rate")]
    public void Invalid_figures_are_rejected(double hours, double rate, double share, string code)
    {
        ShouldFail(() => Deduction.Create(Owner, 1, 7, MarchWorkDate, April, (decimal)hours, (decimal)rate, (decimal)share, Now), code);
    }

    [Fact]
    public void A_line_cannot_apply_in_or_before_its_source_month()
    {
        ShouldFail(() => Deduction.Create(Owner, 1, 7, MarchWorkDate, YearMonth.Parse("2026-03"), 1m, 300m, 50m, Now), "deduction.invalid_apply_month");
    }

    // ---------- Notification and dispute window ----------

    [Fact]
    public void Notifying_opens_the_dispute_window()
    {
        var line = Notified();

        line.Status.Should().Be(DeductionStatus.Notified);
        line.NotifiedAtUtc.Should().Be(Now);
        line.DisputeWindowEndsAtUtc.Should().Be(WindowEnd);
        line.IsDisputeWindowOpen(WindowEnd).Should().BeTrue("the end instant is inclusive");
        line.IsDisputeWindowOpen(WindowEnd.AddTicks(1)).Should().BeFalse();
    }

    [Fact]
    public void A_window_must_end_in_the_future()
    {
        ShouldFail(() => Draft().Notify(Now, Now), "deduction.invalid_window");
    }

    [Fact]
    public void A_line_is_notified_only_once()
    {
        ShouldFail(() => Notified().Notify(Now, WindowEnd), "deduction.invalid_transition");
    }

    [Fact]
    public void The_employee_can_dispute_once_inside_the_window()
    {
        var line = Notified();

        var dispute = line.RaiseDispute(Owner, "  I was on leave that week  ", WindowEnd);

        line.Status.Should().Be(DeductionStatus.Disputed);
        dispute.Reason.Should().Be("I was on leave that week");
        dispute.IsOpen.Should().BeTrue();
        line.Dispute.Should().BeSameAs(dispute);
    }

    [Fact]
    public void A_second_dispute_on_the_same_line_is_rejected()
    {
        var line = Disputed();
        line.ResolveDispute(DisputeOutcome.Rejected, "Timesheets show the work was done", null, null, Manager, Now.AddHours(2));

        ShouldFail(() => line.RaiseDispute(Owner, "Trying again with another reason", Now.AddHours(3)), "deduction.already_disputed");
    }

    [Fact]
    public void Disputes_after_the_window_are_rejected()
    {
        ShouldFail(() => Notified().RaiseDispute(Owner, "I was on leave that week", WindowEnd.AddTicks(1)), "deduction.window_closed");
    }

    [Fact]
    public void Only_the_deducted_employee_can_dispute()
    {
        var act = () => Notified().RaiseDispute(Other, "Not my colleague's fault", Now);

        act.Should().Throw<DomainForbiddenException>().Which.Code.Should().Be("deduction.not_owner");
    }

    [Theory]
    [InlineData("")]
    [InlineData("too short")]
    [InlineData("         ")]
    public void A_dispute_needs_a_reason_of_ten_characters(string reason)
    {
        ShouldFail(() => Notified().RaiseDispute(Owner, reason, Now), "dispute.reason_required");
    }

    [Fact]
    public void Draft_lines_cannot_be_disputed()
    {
        ShouldFail(() => Draft().RaiseDispute(Owner, "I was on leave that week", Now), "deduction.not_disputable");
    }

    // ---------- Dispute resolution ----------

    [Fact]
    public void A_rejected_dispute_leaves_the_line_standing_as_resolved()
    {
        var line = Disputed();

        var replacement = line.ResolveDispute(DisputeOutcome.Rejected, "Timesheets show the work was done", null, null, Manager, Now.AddHours(2));

        replacement.Should().BeNull();
        line.Status.Should().Be(DeductionStatus.Resolved);
        line.AmountSetByDispute.Should().BeTrue();
        line.Dispute!.Outcome.Should().Be(DisputeOutcome.Rejected);
        line.Dispute.ResolvedByEmployeeId.Should().Be(Manager);
        line.Dispute.ResolutionJustification.Should().Be("Timesheets show the work was done");
    }

    [Fact]
    public void An_upheld_dispute_without_figures_cancels_the_line_for_good()
    {
        var line = Disputed();

        var replacement = line.ResolveDispute(DisputeOutcome.Upheld, "Spec changed after delivery", null, null, Manager, Now.AddHours(2));

        replacement.Should().BeNull();
        line.Status.Should().Be(DeductionStatus.Cancelled);
        line.CancelledByDecision.Should().BeTrue();
        line.CancellationReason.Should().Contain("Spec changed after delivery");
    }

    [Fact]
    public void An_upheld_dispute_with_new_figures_replaces_the_line_with_a_smaller_resolved_one()
    {
        var line = Disputed();

        var replacement = line.ResolveDispute(DisputeOutcome.Upheld, "Only half the fix was rework", 2m, 50m, Manager, Now.AddHours(2));

        line.Status.Should().Be(DeductionStatus.Cancelled);
        replacement.Should().NotBeNull();
        replacement!.Status.Should().Be(DeductionStatus.Resolved);
        replacement.Amount.Should().Be(300.00m, "2 h × 300 × 50%");
        replacement.HourlyRateUsed.Should().Be(line.HourlyRateUsed);
        replacement.TimeLogId.Should().Be(line.TimeLogId);
        replacement.DisputeWindowEndsAtUtc.Should().Be(line.DisputeWindowEndsAtUtc);
        replacement.AmountSetByDispute.Should().BeTrue();
    }

    [Theory]
    [InlineData(5.0, null)]
    [InlineData(null, 80.0)]
    [InlineData(4.0, 75.0)]
    public void An_upheld_dispute_can_only_reduce(double? hours, double? share)
    {
        ShouldFail(
            () => Disputed().ResolveDispute(DisputeOutcome.Upheld, "Recalculated after review", (decimal?)hours, (decimal?)share, Manager, Now),
            "dispute.not_a_reduction");
    }

    [Fact]
    public void A_rejected_dispute_cannot_carry_new_figures()
    {
        ShouldFail(
            () => Disputed().ResolveDispute(DisputeOutcome.Rejected, "The deduction stands as is", 1m, null, Manager, Now),
            "dispute.figures_on_reject");
    }

    [Fact]
    public void Resolution_needs_a_justification_and_a_different_person()
    {
        ShouldFail(() => Disputed().ResolveDispute(DisputeOutcome.Rejected, "short", null, null, Manager, Now), "dispute.justification_required");
        var self = () => Disputed().ResolveDispute(DisputeOutcome.Rejected, "I reject my own dispute", null, null, Owner, Now);
        self.Should().Throw<DomainForbiddenException>().Which.Code.Should().Be("dispute.self_resolution");
    }

    [Fact]
    public void A_line_without_an_open_dispute_cannot_be_resolved()
    {
        ShouldFail(() => Notified().ResolveDispute(DisputeOutcome.Rejected, "Nothing to resolve here", null, null, Manager, Now), "deduction.not_disputed");
    }

    [Fact]
    public void An_open_dispute_blocks_cancellation()
    {
        ShouldFail(() => Disputed().CancelByDecision("Cancelling during dispute", Now), "deduction.dispute_open");
    }

    // ---------- Approval ----------

    [Fact]
    public void Approval_inside_the_window_is_allowed_when_the_window_is_not_enforced()
    {
        var line = Notified();

        line.Approve(Manager, WindowEnd.AddDays(-1), enforceDisputeWindow: false);

        line.Status.Should().Be(DeductionStatus.Approved);
    }

    [Fact]
    public void Approval_waits_for_the_window_to_close()
    {
        var line = Notified();

        ShouldFail(() => line.Approve(Manager, WindowEnd), "deduction.window_open");
        line.Approve(Manager, WindowEnd.AddTicks(1));

        line.Status.Should().Be(DeductionStatus.Approved);
        line.ApprovedByEmployeeId.Should().Be(Manager);
    }

    [Fact]
    public void A_resolved_line_can_be_approved_inside_the_window()
    {
        var line = Disputed();
        line.ResolveDispute(DisputeOutcome.Rejected, "Timesheets show the work was done", null, null, Manager, Now.AddHours(2));

        line.Approve(Manager, Now.AddHours(3));

        line.Status.Should().Be(DeductionStatus.Approved);
    }

    [Fact]
    public void A_draft_or_disputed_line_cannot_be_approved()
    {
        ShouldFail(() => Draft().Approve(Manager, Now), "deduction.not_notified");
        ShouldFail(() => Disputed().Approve(Manager, WindowEnd.AddDays(1)), "deduction.invalid_transition");
    }

    [Fact]
    public void Nobody_approves_their_own_deduction()
    {
        var act = () => Notified().Approve(Owner, WindowEnd.AddDays(1));

        act.Should().Throw<DomainForbiddenException>().Which.Code.Should().Be("deduction.self_approval");
    }

    // ---------- Finalization ----------

    [Fact]
    public void Locking_and_applying_moves_an_approved_line_forward()
    {
        var line = Applied();

        line.Status.Should().Be(DeductionStatus.Applied);
        line.PayrollRunId.Should().Be(5);
        line.IsFinalized.Should().BeTrue();
    }

    [Fact]
    public void Only_approved_lines_can_be_locked()
    {
        ShouldFail(() => Notified().Lock(5), "deduction.invalid_transition");
    }

    [Fact]
    public void Finalized_lines_cannot_be_cancelled()
    {
        var locked = Approved();
        locked.Lock(5);

        ShouldFail(() => locked.Cancel("Recalculated after review", Now), "deduction.invalid_transition");
        ShouldFail(() => Applied().CancelByDecision("Changed my mind on this", Now), "deduction.invalid_transition");
    }

    [Fact]
    public void Acknowledgement_policy_blocks_applying_while_the_window_is_open_and_unacknowledged()
    {
        var line = Disputed();
        line.ResolveDispute(DisputeOutcome.Rejected, "Timesheets show the work was done", null, null, Manager, Now.AddHours(1));
        line.Approve(Manager, Now.AddHours(2));
        line.Lock(5);

        ShouldFail(() => line.Apply(requireAcknowledgement: true, Now.AddHours(3)), "deduction.acknowledgement_required");

        line.Acknowledge(Owner, Now.AddHours(4));
        line.Apply(requireAcknowledgement: true, Now.AddHours(5));
        line.Status.Should().Be(DeductionStatus.Applied);
    }

    [Fact]
    public void Acknowledgement_policy_is_satisfied_once_the_window_lapses()
    {
        var line = Approved();
        line.Lock(5);

        line.Apply(requireAcknowledgement: true, WindowEnd.AddTicks(1));

        line.Status.Should().Be(DeductionStatus.Applied);
    }

    [Fact]
    public void Only_the_employee_acknowledges_and_only_notified_onwards()
    {
        var notOwner = () => Notified().Acknowledge(Other, Now);
        notOwner.Should().Throw<DomainForbiddenException>();
        ShouldFail(() => Draft().Acknowledge(Owner, Now), "deduction.not_acknowledgeable");

        var line = Notified();
        line.Acknowledge(Owner, Now.AddHours(1));
        line.Acknowledge(Owner, Now.AddHours(2));
        line.AcknowledgedAtUtc.Should().Be(Now.AddHours(1), "the first acknowledgement is kept");
    }

    // ---------- Compensation ----------

    [Fact]
    public void A_compensating_line_reverses_the_full_amount_and_starts_approved()
    {
        var original = Applied();

        var compensation = Deduction.Compensate(original, YearMonth.Parse("2026-05"), "Bug reclassified as a new requirement", Manager, Now);

        compensation.Kind.Should().Be(DeductionKind.Compensating);
        compensation.Amount.Should().Be(-original.Amount);
        compensation.Hours.Should().Be(original.Hours);
        compensation.Status.Should().Be(DeductionStatus.Approved);
        compensation.ApplyMonth.Should().Be("2026-05");
        compensation.Note.Should().Be("Bug reclassified as a new requirement");
        original.Status.Should().Be(DeductionStatus.Applied, "the original is never edited");
    }

    [Fact]
    public void Only_applied_standard_lines_can_be_compensated()
    {
        ShouldFail(() => Deduction.Compensate(Approved(), April, "Bug reclassified as new", Manager, Now), "deduction.not_compensable");
        var compensation = Deduction.Compensate(Applied(), April, "Bug reclassified as new", Manager, Now);
        ShouldFail(() => Deduction.Compensate(compensation, April, "Bug reclassified as new", Manager, Now), "deduction.not_compensable");
        ShouldFail(() => compensation.Cancel("Undo the compensation", Now), "deduction.compensation_immutable");
    }

    [Fact]
    public void An_applied_line_becomes_reversed_when_its_compensation_is_applied()
    {
        var original = Applied();

        original.MarkReversed(Now);

        original.Status.Should().Be(DeductionStatus.Reversed);
        ShouldFail(() => original.MarkReversed(Now), "deduction.invalid_transition");
    }
}
