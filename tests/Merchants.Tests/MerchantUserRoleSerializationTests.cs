using System.Text.Json;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class MerchantUserRoleSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(MerchantUserRole.Staff);

        Assert.Equal("\"Staff\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<MerchantUserRole>("\"Owner\"");

        Assert.Equal(MerchantUserRole.Owner, value);
    }
}
