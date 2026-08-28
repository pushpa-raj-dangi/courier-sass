using Courier.Ndr.Domain;
using Courier.Shipments.Domain;

namespace Courier.Ndr.Application;

public record UpdateNdrRequest(NdrStatus Status, string? CustomerResponse, string? NextAction, DateTimeOffset? FollowUpAt);

public record ResolveNdrRequest(NdrResolution Resolution, string? RtoReason);

public record NdrResponse(
    Guid Id, Guid ShipmentId, NdrReason Reason, Guid? AssignedStaffUserId, NdrStatus Status,
    string? CustomerResponse, DateTimeOffset? FollowUpAt, string? NextAction, NdrResolution? Resolution, DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt);

public record ReturnShipmentResponse(
    Guid Id, Guid OriginalShipmentId, string ReturnAwb, string RtoReason, Guid OriginBranchId, Guid DestinationBranchId,
    ShipmentStatus CurrentStatus, Guid? CurrentBranchId, DateTimeOffset? MerchantConfirmedAt);

public class NdrValidationException : Exception
{
    public NdrValidationException(string message) : base(message) { }
}
