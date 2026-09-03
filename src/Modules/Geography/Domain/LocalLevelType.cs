using System.Text.Json.Serialization;

namespace Courier.Geography.Domain;

// Same recurring bug class hit in every prior phase (BranchType F2, ShipmentStatus F3,
// ManifestPurpose/ManifestStatus F4, 5 Riders enums F5, 3 Ndr enums F6, 3 Cod enums F7,
// KycDocumentType/MerchantUserRole/PickupRequestStatus F10 per CLARIFICATIONS.md #19):
// LocalLevelType serialized as a raw integer with no JsonStringEnumConverter, so every
// GeographyPicker consumer's LOCAL_LEVEL_TYPE_LABEL[l.type] lookup silently missed (object keyed
// by the string union names, l.type arriving as 0/1/2/3) and rendered an empty "()" suffix —
// found via Frontend Phase F10 Task 16's live E2E pass (staff BranchCreateSheet/
// ShipmentCreateSheet hit this identically; nothing merchant-specific here).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LocalLevelType
{
    Metropolitan,
    SubMetropolitan,
    Municipality,
    RuralMunicipality
}
