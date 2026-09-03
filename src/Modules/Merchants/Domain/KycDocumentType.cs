using System.Text.Json.Serialization;

namespace Courier.Merchants.Domain;

// See Branches.Domain.BranchType for why this is a per-enum attribute rather than a global
// converter. Same fix as every prior phase's own enum-serialization gap (BranchType F2,
// ShipmentStatus F3, ManifestPurpose/ManifestStatus F4, 5 Riders enums F5, 3 Ndr enums F6,
// 3 Cod enums F7) — KycStatus in this same folder already had it; this one didn't.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum KycDocumentType
{
    CitizenshipDocument,
    PanCard,
    BusinessRegistrationCertificate,
    BankAccountProof,
    Other
}
