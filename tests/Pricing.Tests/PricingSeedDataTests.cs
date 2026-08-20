using Courier.Pricing.Infrastructure;
using Xunit;

namespace Courier.Pricing.Tests;

public class PricingSeedDataTests
{
    [Fact]
    public void ServiceTypes_AllCodesAreDistinct()
    {
        var codes = PricingSeedData.ServiceTypes.Select(s => s.Code).ToList();
        Assert.Equal(codes.Distinct().Count(), codes.Count);
    }

    [Fact]
    public void WeightSlabs_AreContiguousWithNoGapsOrOverlaps()
    {
        var slabs = PricingSeedData.WeightSlabs.OrderBy(s => s.MinWeightKg).ToList();
        for (var i = 0; i < slabs.Count - 1; i++)
        {
            Assert.Equal(slabs[i].MaxWeightKg, slabs[i + 1].MinWeightKg);
        }
        Assert.Null(slabs[^1].MaxWeightKg);
    }

    [Fact]
    public void PricingIds_SameInputProducesSameGuid()
    {
        var first = PricingIds.ServiceTypeId(Courier.Pricing.Domain.ServiceTypeCode.Standard);
        var second = PricingIds.ServiceTypeId(Courier.Pricing.Domain.ServiceTypeCode.Standard);
        Assert.Equal(first, second);
    }
}
