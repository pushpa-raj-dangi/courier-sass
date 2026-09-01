using System.Text.Json;
using Courier.Ndr.Domain;
using Xunit;

namespace Courier.Ndr.Tests;

public class NdrEnumSerializationTests
{
    [Fact]
    public void NdrStatus_SerializesAsString() =>
        Assert.Equal("\"Open\"", JsonSerializer.Serialize(NdrStatus.Open));

    [Fact]
    public void NdrReason_SerializesAsString() =>
        Assert.Equal("\"CustomerUnavailable\"", JsonSerializer.Serialize(NdrReason.CustomerUnavailable));

    [Fact]
    public void NdrResolution_SerializesAsString() =>
        Assert.Equal("\"Reattempt\"", JsonSerializer.Serialize(NdrResolution.Reattempt));
}
