using System.Text.Json;
using Courier.Merchants.Domain;
using Xunit;

namespace Courier.Merchants.Tests;

public class KycDocumentTypeSerializationTests
{
    [Fact]
    public void SerializesAsMemberName_NotRawInteger()
    {
        var json = JsonSerializer.Serialize(KycDocumentType.PanCard);

        Assert.Equal("\"PanCard\"", json);
    }

    [Fact]
    public void RoundTrips_FromMemberNameString()
    {
        var value = JsonSerializer.Deserialize<KycDocumentType>("\"BusinessRegistrationCertificate\"");

        Assert.Equal(KycDocumentType.BusinessRegistrationCertificate, value);
    }
}
