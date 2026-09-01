using Courier.Riders.Domain;

namespace Courier.Riders.Application;

public record CreateRiderRequest(string Email, string Password, string FullName, string? Phone, string LicenseNumber, Guid BaseBranchId, RiderRoleType RoleType);

public record RiderResponse(Guid Id, Guid UserId, Guid BaseBranchId, string LicenseNumber, RiderRoleType RoleType, bool IsActive);

public record CreateVehicleRequest(Guid? RiderId, string Type, string PlateNumber, decimal? Capacity);

public record VehicleResponse(Guid Id, Guid? RiderId, string Type, string PlateNumber, decimal? Capacity);

public record AssignDeliveryTaskRequest(Guid RiderId, Guid ShipmentId, DeliveryTaskType Type);

public record DeliveryTaskResponse(Guid Id, Guid DeliveryRunId, Guid ShipmentId, DeliveryTaskType Type, int Sequence, DeliveryTaskStatus Status);

public record RiderScanRequest(string Awb);

public record RecordFailedAttemptRequest(Guid ShipmentId, DeliveryAttemptReason Reason, string? Notes, decimal? Latitude, decimal? Longitude, bool CustomerContacted, string? NextAction);

public enum PodFileKind { Signature, Photo }

public record UploadPodFileResponse(string StorageKey);

public record CompleteDeliveryRequest(Guid ShipmentId, string RecipientName, bool OtpVerified, string? SignatureStorageKey, string? PhotoStorageKey, decimal? Latitude, decimal? Longitude, decimal? CollectedCodAmount = null);

public record DeliveryAttemptResponse(Guid Id, Guid ShipmentId, Guid RiderId, int AttemptNumber, bool Succeeded, DeliveryAttemptReason? Reason, string? NextAction);

public class RiderValidationException : Exception
{
    public RiderValidationException(string message) : base(message) { }
}

public class RiderNotFoundException : Exception
{
    public RiderNotFoundException(Guid riderId) : base($"Rider {riderId} was not found.")
    {
        RiderId = riderId;
    }

    public Guid RiderId { get; }
}

public record MyTaskResponse(Guid TaskId, Guid ShipmentId, DeliveryTaskType Type, int Sequence, string Awb, string RecipientName, string RecipientPhone, string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount);

public record MyTasksTodayResponse(Guid? DeliveryRunId, List<MyTaskResponse> Tasks);
