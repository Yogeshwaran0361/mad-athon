using QuestPDF.Infrastructure;

namespace ReworkGuard.Infrastructure.Pdf;

/// <summary>QuestPDF needs a license choice before first use. The Community license covers organisations under its revenue threshold.</summary>
public static class QuestPdfLicense
{
    public static void EnsureConfigured() => QuestPDF.Settings.License ??= LicenseType.Community;
}
