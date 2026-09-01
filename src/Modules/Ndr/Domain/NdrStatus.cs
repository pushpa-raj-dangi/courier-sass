using System.Text.Json.Serialization;

namespace Courier.Ndr.Domain;

// Exact list from CLAUDE.md §20. Legal forward moves (enforced in NdrService.UpdateAsync, Task 7):
// Open -> Contacting -> CustomerConfirmed -> {ReattemptScheduled | RtoRequested} -> Resolved.
// No backward or skipped moves.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NdrStatus
{
    Open,
    Contacting,
    CustomerConfirmed,
    ReattemptScheduled,
    RtoRequested,
    Resolved
}
