using ReworkGuard.Domain.Common;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Domain.Lifecycle;
using static ReworkGuard.Tests.Domain.DomainFactory;

namespace ReworkGuard.Tests.Domain;

public sealed class DefectReviewTests
{
    public static TheoryData<bool, bool?, RootCause?, bool> DeductibilityMatrix()
    {
        var data = new TheoryData<bool, bool?, RootCause?, bool>();
        foreach (var within in new[] { true, false })
        {
            foreach (var caused in new bool?[] { true, false, null })
            {
                foreach (var cause in Enum.GetValues<RootCause>().Cast<RootCause?>().Append(null))
                {
                    data.Add(within, caused, cause, within && caused == true && cause == RootCause.OwnDefect);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DeductibilityMatrix))]
    public void Only_an_own_defect_caused_by_the_delivery_inside_warranty_is_deductible(bool within, bool? caused, RootCause? cause, bool expected)
    {
        DeductibilityPolicy.IsDeductible(within, caused, cause, qaMissDeductible: false).Should().Be(expected);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void QaMiss_is_deductible_only_when_the_policy_allows_it(bool qaMissDeductible, bool expected)
    {
        DeductibilityPolicy.IsDeductible(true, true, RootCause.QaMiss, qaMissDeductible).Should().Be(expected);
        DeductibilityPolicy.IsDeductible(false, true, RootCause.QaMiss, qaMissDeductible).Should().BeFalse();
        DeductibilityPolicy.IsDeductible(true, false, RootCause.QaMiss, qaMissDeductible).Should().BeFalse();
    }

    [Fact]
    public void An_own_defect_review_needs_no_justification_and_becomes_deductible()
    {
        var bug = ReopenedBug();

        bug.Review(RootCause.OwnDefect, causedByOriginalDelivery: true, null, ManagerId, Completed.AddDays(5));

        bug.IsDeductible(qaMissDeductible: false).Should().BeTrue();
        bug.ReviewedByEmployeeId.Should().Be(ManagerId);
        bug.ReviewedAtUtc.Should().Be(Completed.AddDays(5));
    }

    [Theory]
    [InlineData(RootCause.OwnDefect, false)]
    [InlineData(RootCause.NewRequirement, true)]
    [InlineData(RootCause.SpecAmbiguity, false)]
    [InlineData(RootCause.ExternalIssue, true)]
    [InlineData(RootCause.QaMiss, true)]
    public void Clearing_the_deliverer_or_naming_another_cause_requires_ten_characters_of_justification(RootCause cause, bool caused)
    {
        var bug = ReopenedBug();

        var missing = () => bug.Review(cause, caused, "  too short ", ManagerId, Completed);
        missing.Should().Throw<DomainException>().Which.Code.Should().Be("bug.review_justification_required");
        bug.IsReviewed.Should().BeFalse();

        bug.Review(cause, caused, "Requirement changed by the client after sign-off", ManagerId, Completed);
        bug.IsReviewed.Should().BeTrue();
        bug.IsDeductible(qaMissDeductible: false).Should().BeFalse();
    }

    private static ReworkGuard.Domain.Entities.Bug ReopenedBug() => TaskIn(WorkTaskStatus.Reopened).Bugs.Single();
}
