using ClosedXML.Excel;
using ReworkGuard.Application.Reports;

namespace ReworkGuard.Infrastructure.Reports;

/// <summary>One worksheet: a bold header row (frozen) and typed cells, decimals formatted to two places.</summary>
public sealed class XlsxReportRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Xlsx;

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public string Extension => "xlsx";

    public byte[] Render(ReportTable table)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(table.Title.Length > 31 ? table.Title[..31] : table.Title);
        for (var c = 0; c < table.Columns.Count; c++)
        {
            sheet.Cell(1, c + 1).SetValue(table.Columns[c].Header);
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                switch (row[c])
                {
                    case decimal d:
                        cell.SetValue(d);
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case int i:
                        cell.SetValue(i);
                        break;
                    case var other:
                        // SetValue stores text as a string cell, never a formula, so cell text cannot inject one.
                        cell.SetValue(other?.ToString() ?? string.Empty);
                        break;
                }
            }
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
