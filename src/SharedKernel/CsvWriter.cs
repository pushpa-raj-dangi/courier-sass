using System.Text;

namespace Courier.SharedKernel;

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

    public static IEnumerable<string> WriteLines(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        yield return string.Join(',', headers.Select(EscapeField)) + "\r\n";
        foreach (var row in rows)
        {
            yield return string.Join(',', row.Select(EscapeField)) + "\r\n";
        }
    }

    // Guard against CSV formula injection: a field beginning with =, +, -, @, a tab, or a carriage
    // return can be interpreted as a formula (or otherwise mishandled) when the exported CSV is opened
    // in Excel/LibreOffice. Prefixing with a single quote forces those tools to treat it as literal text.
    private static string EscapeField(string? field)
    {
        field ??= string.Empty;
        if (field.Length > 0 && "=+-@\t\r".Contains(field[0]))
        {
            field = "'" + field;
        }
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
