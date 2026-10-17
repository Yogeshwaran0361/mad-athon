namespace ReworkGuard.Domain.Lifecycle;

public static class WarrantyPolicy
{
    /// <summary>The last day (inclusive) on which a defect still counts against the original delivery.</summary>
    public static DateOnly LastWarrantyDay(DateOnly completionDate, int warrantyDays) => completionDate.AddDays(warrantyDays);

    public static bool IsWithinWarranty(DateOnly completionDate, int warrantyDays, DateOnly foundDate) =>
        foundDate <= LastWarrantyDay(completionDate, warrantyDays);

    public static bool HasExpired(DateOnly completionDate, int warrantyDays, DateOnly today) =>
        today > LastWarrantyDay(completionDate, warrantyDays);
}
