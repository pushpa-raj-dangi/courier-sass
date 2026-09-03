// Mirrors src/Modules/Reports/Infrastructure/CsvWriter.EscapeField exactly (RFC 4180 quoting
// plus a formula-injection guard for Excel/LibreOffice) — this feature has no backend CSV export
// endpoint of its own (design spec's resolved decision: MerchantSettlement itself is the
// invoice), so the same escaping discipline is reimplemented here against already-fetched JSON.
function escapeField(field: string): string {
  let value = field;
  if (value.length > 0 && "=+-@\t\r".includes(value[0])) {
    value = "'" + value;
  }
  if (value.includes(",") || value.includes('"') || value.includes("\n") || value.includes("\r")) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}

export function toCsv(headers: string[], rows: string[][]): string {
  const lines = [headers, ...rows].map((row) => row.map(escapeField).join(","));
  return lines.join("\r\n") + "\r\n";
}
