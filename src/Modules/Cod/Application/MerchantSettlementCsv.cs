using System.Globalization;
using Courier.SharedKernel;

namespace Courier.Cod.Application;

public static class MerchantSettlementCsv
{
    private static readonly string[] Headers = ["AWB", "COD amount", "Courier fee", "Other deductions", "Net payable"];

    public static IEnumerable<string> Lines(MerchantSettlementResponse settlement)
    {
        var rows = settlement.Items
            .Select(i => (IReadOnlyList<string?>)new[] { i.ShipmentAwb, Fmt(i.CODAmount), Fmt(i.CourierFee), Fmt(i.OtherDeductions), Fmt(i.NetPayable) })
            .Append(new[]
            {
                "Total",
                Fmt(settlement.Items.Sum(i => i.CODAmount)),
                Fmt(settlement.Items.Sum(i => i.CourierFee)),
                Fmt(settlement.Items.Sum(i => i.OtherDeductions)),
                Fmt(settlement.TotalPayable),
            });
        return CsvWriter.WriteLines(Headers, rows);
    }

    private static string Fmt(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
