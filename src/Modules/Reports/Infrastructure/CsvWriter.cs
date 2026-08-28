using System.Text;

namespace Courier.Reports.Infrastructure;

public static class CsvWriter
{
    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', headers.Select(EscapeField))).Append("\r\n");
        foreach (var row in rows)
        {
            sb.Append(string.Join(',', row.Select(EscapeField))).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string EscapeField(string? field)
    {
        field ??= string.Empty;
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
