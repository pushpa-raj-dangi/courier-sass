using Courier.Manifests.Domain;

namespace Courier.Manifests.Application;

public record CreateManifestRequest(Guid FromBranchId, Guid ToBranchId, ManifestPurpose Purpose = ManifestPurpose.Forward);

public record ManifestResponse(
    Guid Id,
    string Code,
    Guid FromBranchId,
    Guid ToBranchId,
    ManifestPurpose Purpose,
    string? VehiclePlate,
    Guid? DriverId,
    ManifestStatus Status,
    int ShipmentCount,
    DateTimeOffset CreatedAt,
    // F15 R3b-1: parcels receive-scanned at the destination (ManifestShipment.ReceivedAt != null).
    int ReceivedCount);

public record ScanShipmentRequest(string Awb);

public record ManifestShipmentResponse(Guid Id, Guid ShipmentId, string Awb, DateTimeOffset ScannedAt, DateTimeOffset? ReceivedAt);

public record DispatchManifestRequest(string VehiclePlate, Guid? DriverId);

public record MissingShipmentResponse(Guid ShipmentId, string Awb, DateTimeOffset ScannedAt);

public class ManifestValidationException : Exception
{
    public ManifestValidationException(string message) : base(message) { }
}

public class ManifestNotFoundException : Exception
{
    public ManifestNotFoundException(Guid manifestId) : base($"Manifest {manifestId} was not found.")
    {
        ManifestId = manifestId;
    }

    public Guid ManifestId { get; }
}

// Thrown by ScanAsync/ReceiveScanAsync for any BRANCH-OPERATIONS.md §1 five-check rejection —
// carries a stable ReasonCode so the Presentation layer and API callers can branch on the specific
// failure mode (SHIPMENT_NOT_FOUND, WRONG_STATE, WRONG_CUSTODY, MANIFEST_NOT_OPEN,
// MANIFEST_NOT_RECEIVING, NOT_ON_MANIFEST, ALREADY_RECEIVED).
public class ManifestScanRejectedException : ManifestValidationException
{
    public ManifestScanRejectedException(string reasonCode, string message) : base(message)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}
