using Courier.Cod.Application;
using Courier.Cod.Domain;
using Xunit;

namespace Courier.Cod.Tests;

public class MerchantSettlementCsvTests
{
    private static MerchantSettlementResponse Settlement(params MerchantSettlementItemResponse[] items) =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
            items.Sum(i => i.NetPayable), MerchantSettlementStatus.Created, null, items.ToList());

    private static MerchantSettlementItemResponse Item(string awb, decimal cod, decimal fee, decimal other = 0m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), awb, cod, fee, other, cod - fee - other);

    [Fact]
    public void Lines_WritesHeaderItemsAndTotal()
    {
        var lines = MerchantSettlementCsv.Lines(Settlement(Item("NX-1", 2500m, 150m), Item("NX-2", 990m, 100m))).ToList();

        Assert.Equal(new[]
        {
            "AWB,COD amount,Courier fee,Other deductions,Net payable\r\n",
            "NX-1,2500.00,150.00,0.00,2350.00\r\n",
            "NX-2,990.00,100.00,0.00,890.00\r\n",
            "Total,3490.00,250.00,0.00,3240.00\r\n",
        }, lines);
    }

    [Fact]
    public void Lines_NegativeNet_IsNotGuarded()
    {
        var lines = MerchantSettlementCsv.Lines(Settlement(Item("NX-3", 50m, 100m))).ToList();

        Assert.Equal("NX-3,50.00,100.00,0.00,-50.00\r\n", lines[1]);
        Assert.Equal("Total,50.00,100.00,0.00,-50.00\r\n", lines[2]);
    }

    [Fact]
    public void Lines_GuardsFormulaInjectionInAwb()
    {
        var lines = MerchantSettlementCsv.Lines(Settlement(Item("=HYPERLINK", 100m, 10m))).ToList();

        Assert.StartsWith("'=HYPERLINK,", lines[1]);
    }

    [Fact]
    public void FileName_PeriodStartingAtNptMidnight_UsesNptDate()
    {
        var npt = TimeZoneInfo.CreateCustomTimeZone("Asia/Kathmandu", TimeSpan.FromMinutes(345), "Nepal Time", "Nepal Time");
        // 2026-11-01 00:00 NPT is 2026-10-31 18:15 UTC; 2026-11-15 23:59 NPT is 2026-11-15 18:14 UTC.
        var settlement = new MerchantSettlementResponse(Guid.NewGuid(), Guid.NewGuid(),
            new DateTimeOffset(2026, 10, 31, 18, 15, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 15, 18, 14, 0, TimeSpan.Zero),
            0m, MerchantSettlementStatus.Created, null, new List<MerchantSettlementItemResponse>());

        Assert.Equal("settlement-20261101-20261115.csv", MerchantSettlementCsv.FileName(settlement, npt));
        Assert.Equal("settlement-20261031-20261115.csv", MerchantSettlementCsv.FileName(settlement, TimeZoneInfo.Utc));
    }
}
