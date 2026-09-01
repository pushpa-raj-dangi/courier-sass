using System.Text.Json;
using Courier.Cod.Domain;
using Xunit;

namespace Courier.Cod.Tests;

public class CodEnumSerializationTests
{
    [Fact]
    public void CodCollectionStatus_SerializesAsString() =>
        Assert.Equal("\"CodExpected\"", JsonSerializer.Serialize(CodCollectionStatus.CodExpected));

    [Fact]
    public void MerchantSettlementStatus_SerializesAsString() =>
        Assert.Equal("\"Settled\"", JsonSerializer.Serialize(MerchantSettlementStatus.Settled));

    [Fact]
    public void FinancialTransactionType_SerializesAsString() =>
        Assert.Equal("\"CodVerification\"", JsonSerializer.Serialize(FinancialTransactionType.CodVerification));
}
