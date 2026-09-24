using System.Text.Json;
using Courier.International.Application.Carriers;

namespace Courier.International.Tests.Carriers;

public class CarrierPayloadSanitizerTests
{
    [Fact]
    public void ToStoredJson_MasksSecretValuesAndKeepsValidJson()
    {
        var stored = CarrierPayloadSanitizer.ToStoredJson("{\"apiKey\":\"sk_live_12345\",\"awb\":\"A1\"}", new[] { "sk_live_12345" });

        Assert.Equal("{\"apiKey\":\"***\",\"awb\":\"A1\"}", stored);
        using var _ = JsonDocument.Parse(stored!);
    }

    [Fact]
    public void ToStoredJson_NonJsonText_IsStoredAsAJsonString()
    {
        var stored = CarrierPayloadSanitizer.ToStoredJson("Bad Gateway from sk_live_12345", new[] { "sk_live_12345" });

        Assert.Equal("\"Bad Gateway from ***\"", stored);
    }

    [Fact]
    public void ToStoredJson_Null_IsNull()
    {
        Assert.Null(CarrierPayloadSanitizer.ToStoredJson(null, Array.Empty<string>()));
    }
}
