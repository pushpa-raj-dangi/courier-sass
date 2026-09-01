using System.Text.Json.Serialization;

namespace Courier.Cod.Domain;

// COD-LIFECYCLE.md §1's table links FinancialTransaction to exactly one step
// (BRANCH_RECONCILED -> FINANCE_VERIFIED); single member by design, not an oversight — easy to
// extend if a future phase gives FinancialTransaction a second real use.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FinancialTransactionType
{
    CodVerification
}
