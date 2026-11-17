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
    public void Lines_GuardsFormulaInjectionInAwb()
    {
        var lines = MerchantSettlementCsv.Lines(Settlement(Item("=HYPERLINK", 100m, 10m))).ToList();

        Assert.StartsWith("'=HYPERLINK,", lines[1]);
    }
}
