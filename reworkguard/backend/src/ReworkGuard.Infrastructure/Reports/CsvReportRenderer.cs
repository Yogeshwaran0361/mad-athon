using System.Globalization;
using System.Text;
using ReworkGuard.Application.Reports;

namespace ReworkGuard.Infrastructure.Reports;

/// <summary>
/// RFC 4180 CSV (CRLF line breaks, fields with commas, quotes or line breaks quoted, quotes doubled), UTF-8 with a BOM
/// so Excel detects the encoding. Text cells that a spreadsheet would treat as a formula (starting with =, +, -, @,
/// tab or carriage return) are prefixed with an apostrophe.
/// </summary>
public sealed class CsvReportRenderer : IReportRenderer
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] NeedsQuoting = [',', '"', '\r', '\n'];

    public ReportFormat Format => ReportFormat.Csv;

    public string ContentType => "text/csv";

    public string Extension => "csv";

    public byte[] Render(ReportTable table)
    {
        var builder = new StringBuilder();
        AppendLine(builder, table.Columns.Select(c => Text(c.Header)));
        foreach (var row in table.Rows)
        {
            AppendLine(builder, row.Select(Cell));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        return [.. preamble, .. body];
    }

    public static string Text(string value)
    {
        var guarded = value.Length > 0 && FormulaStarts.Contains(value[0]) ? "'" + value : value;
        return guarded.IndexOfAny(NeedsQuoting) >= 0 ? "\"" + guarded.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : guarded;
    }

    private static string Cell(object cell) => cell switch
    {
        decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => Text(Convert.ToString(cell, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static void AppendLine(StringBuilder builder, IEnumerable<string> fields)
    {
        builder.AppendJoin(',', fields);
        builder.Append("\r\n");
    }
}
