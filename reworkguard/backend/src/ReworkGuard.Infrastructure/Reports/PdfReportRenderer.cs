using ReworkGuard.Infrastructure.Pdf;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ReworkGuard.Application.Reports;

namespace ReworkGuard.Infrastructure.Reports;

/// <summary>A landscape A4 table with the title, subtitle, header row and page numbers. Uses the QuestPDF Community license.</summary>
public sealed class PdfReportRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Pdf;

    public string ContentType => "application/pdf";

    public string Extension => "pdf";

    public byte[] Render(ReportTable table)
    {
        QuestPdfLicense.EnsureConfigured();
        return Document.Create(container => container.Page(page =>
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(24);
        page.DefaultTextStyle(t => t.FontSize(8));
        page.Header().Column(column =>
        {
            column.Item().Text(table.Title).FontSize(14).Bold();
            column.Item().PaddingBottom(6).Text(table.Subtitle).FontColor(Colors.Grey.Darken1);
        });
        page.Content().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                foreach (var column in table.Columns)
                {
                    if (column.Kind == ReportColumnKind.Text)
                    {
                        columns.RelativeColumn(2);
                    }
                    else
                    {
                        columns.RelativeColumn();
                    }
                }
            });
            grid.Header(header =>
            {
                foreach (var column in table.Columns)
                {
                    var cell = header.Cell().Background(Colors.Grey.Lighten3).Padding(3);
                    (column.Kind == ReportColumnKind.Text ? cell : cell.AlignRight()).Text(column.Header).Bold();
                }
            });
            foreach (var row in table.Rows)
            {
                foreach (var value in row)
                {
                    var cell = grid.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
                    (value is string ? cell : cell.AlignRight()).Text(FormatCell(value));
                }
            }
        });
        page.Footer().AlignRight().Text(text =>
        {
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
        });
    })).GeneratePdf();
    }

    private static string FormatCell(object value) => value switch
    {
        decimal d => d.ToString("#,##0.00", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
