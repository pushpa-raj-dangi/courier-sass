using Courier.International.Application.Carriers;
using Courier.International.Infrastructure.Carriers;

namespace Courier.International.Tests.Carriers;

public class CarrierProviderRegistryTests
{
    [Fact]
    public void Find_IsCaseInsensitive_AndUnknownIsNull()
    {
        var registry = new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider() });

        Assert.IsType<ManualCarrierProvider>(registry.Find("manual"));
        Assert.Null(registry.Find("DHL_EXPRESS"));
        Assert.Equal(new[] { "MANUAL" }, registry.RegisteredCodes);
    }

    [Fact]
    public void DuplicateCarrierCodes_Throw()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CarrierProviderRegistry(new ICarrierProvider[] { new ManualCarrierProvider(), new ManualCarrierProvider() }));
    }
}
