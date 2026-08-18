# Domain Model

Entities grouped by owning module (`ARCHITECTURE.md` §2). Fields listed are the
core/required set, not exhaustive column lists — timestamps (`CreatedAt`/`UpdatedAt`)
and soft-delete flags are omitted below where obvious but assumed on every entity
unless noted. `OrganizationId` is present on every tenant-owned entity (marked once
per module rather than repeated per entity) and is enforced by the global query
filter (`ARCHITECTURE.md` §3) — it is never optional and never client-supplied.

Entities/fields introduced or changed by `CLARIFICATIONS.md` are marked **[C#n]**.

---

## Identity & Tenancy — `OrganizationId` N/A (this module defines it)

```text
Organization { Id, Name, Slug, SubscriptionPlan, IsActive }

User { Id, OrganizationId (nullable — null = platform user, e.g. SuperAdmin) [C#5],
       Email, PasswordHash, FullName, Phone, IsActive }

Role { Id, OrganizationId (nullable — null = platform-level role) [C#5],
       Name, IsSystemDefault }

Permission { Id, Key ("shipment.create" etc.), Description }

RolePermission { RoleId, PermissionId }

UserRole { Id, UserId, RoleId, OrganizationId, BranchScope (SpecificBranches|AllBranches),
           AssignedAt, AssignedByUserId }                                    [C#1]

UserBranch { Id, UserId, BranchId, OrganizationId,
             GrantedAt, ExpiresAt (nullable), GrantedByUserId }              [C#6]

RefreshToken { Id, UserId, TokenHash, ExpiresAt, RevokedAt, ReplacedByTokenId }
```

## Geography — `OrganizationId` N/A (shared reference data)

```text
Province { Id, Name }
District { Id, ProvinceId, Name }
LocalLevel { Id, DistrictId, Name, Type (Metropolitan|SubMetropolitan|
             Municipality|RuralMunicipality) }                               [C#2]
Ward { Id, LocalLevelId, Number }
Area { Id, WardId, Name }
DeliveryZone { Id, OrganizationId, Name, CoveredAreaIds[] }
```

`DeliveryZone` is org-owned (each courier defines its own zone groupings over the
shared geography); everything above it is shared reference data seeded once.

## Branches — `OrganizationId` present

```text
Branch { Id, OrganizationId, Code, Name, Type (HeadOffice|Hub|Branch|PickupPoint|
         Warehouse), ProvinceId, DistrictId, LocalLevelId, WardNumber,       [C#2]
         AddressLine, Latitude, Longitude, Phone, OperatingHours,
         ParentBranchId (nullable, self-FK), IsActive }
```

## Merchants — `OrganizationId` present

```text
Merchant { Id, OrganizationId, LegalName, DisplayName, ContactPhone, ContactEmail,
           KycStatus (Pending|Approved|Rejected), IsActive }

MerchantUser { Id, MerchantId, OrganizationId, Email, PasswordHash, FullName, IsActive }

MerchantAddress { Id, MerchantId, Label, ProvinceId, DistrictId, LocalLevelId,
                   WardNumber, AddressLine, Latitude, Longitude, IsDefaultPickup }

MerchantKycDocument { Id, MerchantId, DocumentType, StorageKey, Status, ReviewedByUserId }

ApiKey { Id, MerchantId, OrganizationId (denormalized, kept consistent with
         Merchant.OrganizationId), HashedKey, Prefix, Scopes[], IsActive,
         CreatedAt, RevokedAt }                                              [C#7]
```

## Pricing — `OrganizationId` present (except shared `ServiceType`/`WeightSlab` templates)

```text
ServiceType { Id, Code (Standard|Express|SameDay|Heavy|Special), Name }
WeightSlab { Id, MinWeightKg, MaxWeightKg }
Zone { Id, OrganizationId, Name }
ZoneRate { Id, RateCardId, OriginZoneId, DestinationZoneId, ServiceTypeId,
           WeightSlabId, BaseRate }
AdditionalCharge { Id, RateCardId, Type (Insurance|COD|Fuel|Other), CalculationType
                    (Flat|Percentage), Value }
RateCard { Id, OrganizationId, Name, EffectiveFrom, EffectiveTo (nullable),
           Version, IsActive }
RateCardRule { Id, RateCardId, ... } # composes ZoneRate/AdditionalCharge into an
                                       # evaluable rule set for the pricing engine
MerchantRateCard { Id, MerchantId, RateCardId, OverrideDiscountPercent (nullable) }
```

`RateCard` is immutable once shipments have priced against it — a rate change is a
new `RateCard` version with a new `EffectiveFrom`, never an in-place edit
(CLAUDE.md §16).

## Shipments & Tracking — `OrganizationId` present

```text
Shipment { Id (internal UUID), Awb (business identifier, unique per org),
           OrganizationId, MerchantId,
           OriginBranchId, DestinationBranchId, CurrentBranchId, CurrentCustodianId
             (nullable FK — User or Rider, see Custody note below),
           CurrentStatus, ServiceTypeId,
           Weight, LengthCm, WidthCm, HeightCm, DeclaredValue,
           CODAmount, DeliveryCharge, InsuranceCharge, OtherCharges, TotalCharge,
             # ^ all locked at booking time from the RateCard/ZoneRate resolved
           RecipientName, RecipientPhone, RecipientAddress (structured, geography FKs),
           SenderName, SenderPhone, SenderAddress (structured, geography FKs) }

ShipmentItem { Id, ShipmentId, Description, Quantity, UnitValue }

ShipmentEvent { Id, ShipmentId, EventType, PreviousStatus, NewStatus, BranchId,
                UserId (nullable), RiderId (nullable), Timestamp, Latitude, Longitude,
                Notes, Metadata } # append-only, never updated/deleted
```

**Custody note:** `CurrentCustodianId` is polymorphic (a branch-based staff user or a
rider) in practice modeled as two nullable FKs (`CurrentCustodianUserId`,
`CurrentCustodianRiderId`) with exactly one non-null at a time, rather than a true
polymorphic FK — kept explicit for referential integrity rather than using a
type+id pair.

## Scanning & Manifests — `OrganizationId` present

```text
Manifest { Id, OrganizationId, Code, FromBranchId, ToBranchId, VehiclePlate,
           DriverId (nullable FK to Rider or a Driver concept — reuse Rider unless a
           dedicated Driver entity proves necessary), Status (Draft|Sealed|
           Dispatched|InTransit|Received|Closed), ShipmentCount, CreatedByUserId }

ManifestShipment { Id, ManifestId, ShipmentId, ScannedAt, ReceivedAt (nullable) }

ShipmentTransfer { Id, ManifestId, ShipmentId, FromBranchId, ToBranchId, Timestamp }
```

## Riders & Delivery — `OrganizationId` present

```text
Rider { Id, OrganizationId, UserId (FK — a rider is also a User with the
        PickupRider/DeliveryRider role), BaseBranchId, LicenseNumber, IsActive }

Vehicle { Id, OrganizationId, RiderId (nullable), Type, PlateNumber, Capacity }

RiderAssignment { Id, RiderId, BranchId, AssignedAt, UnassignedAt (nullable) }

DeliveryRun { Id, RiderId, BranchId, Date, Status }

DeliveryTask { Id, DeliveryRunId, ShipmentId, Type (Pickup|Delivery), Sequence,
               Status }

DeliveryAttempt { Id, ShipmentId, RiderId, AttemptNumber, AttemptedAt,
                   Reason (CustomerUnavailable|PhoneUnreachable|WrongAddress|
                   IncompleteAddress|CustomerRefused|CustomerRequestedReschedule|
                   CashUnavailable|DamagedParcel|Other),
                   Notes, Latitude, Longitude, CustomerContacted, NextAction }

DeliveryProof { Id, ShipmentId, DeliveryAttemptId, OtpVerified, SignatureStorageKey,
                PhotoStorageKey, Latitude, Longitude, Timestamp, RiderId,
                RecipientName, CorrectedAt (nullable), CorrectionReason (nullable) }
                # immutable after success except via the controlled correction
                # fields above, which never overwrite the original capture
```

## NDR — `OrganizationId` present

```text
NDR { Id, ShipmentId, OrganizationId, Reason (from DeliveryAttempt.Reason),
      AssignedStaffUserId, Status (Open|Contacting|CustomerConfirmed|
      ReattemptScheduled|RtoRequested|Resolved), CustomerResponse, FollowUpAt,
      NextAction, Resolution (Reattempt|Rto|AutoRto|Other), ResolvedAt }         [C#4]
```

## RTO — `OrganizationId` present

```text
ReturnShipment { Id, OriginalShipmentId, OrganizationId, ReturnAwb,
                  RtoReason, OriginBranchId, DestinationBranchId, CurrentBranchId,
                  Status, ReceivedAt (nullable), MerchantConfirmedAt (nullable) }
```

## COD & Finance — `OrganizationId` present

```text
CODCollection { Id, ShipmentId, OrganizationId, ExpectedAmount, CollectedAmount,
                 Status (Expected|Collected), CollectedByRiderId, CollectedAt,
                 DeliveryProofId }

RiderCashSettlement { Id, RiderId, BranchId, DeclaredAmount, ShipmentCount,
                        SettledAt, ConfirmedByUserId }

BranchCashSettlement { Id, BranchId, ConfirmedAmount, RiderCashSettlementIds[],
                         ReconciledByUserId, ReconciledAt }

FinancialTransaction { Id, OrganizationId, Type, Amount, ReferenceEntityType,
                         ReferenceEntityId, VerifiedByUserId, VerifiedAt }

MerchantSettlement { Id, MerchantId, OrganizationId, PeriodFrom, PeriodTo,
                       TotalPayable, Status (Created|Settled), SettledAt }

MerchantSettlementItem { Id, MerchantSettlementId, ShipmentId, CODAmount,
                           CourierFee, OtherDeductions, NetPayable }

Adjustment { Id, OrganizationId, EntityType, EntityId, BeforeValue, AfterValue,
              Reason, RequestedByUserId, RequestedAt, ApprovedByUserId (nullable),
              ApprovedAt (nullable) }
```

## Notifications — `OrganizationId` present

```text
NotificationTemplate { Id, OrganizationId, EventType, Channel (SMS|Email|WhatsApp|
                         Push), Body, IsActive }

NotificationLog { Id, OrganizationId, EventType, Channel, RecipientAddress, Status,
                    SentAt, ProviderResponse }

OutboxMessage { Id, OrganizationId, EventType, Payload, CreatedAt, PublishedAt
                 (nullable), Attempts }   # backs both Notifications and Webhooks
                                            # per ARCHITECTURE.md's outbox pattern
```

## Webhooks — `OrganizationId` present

```text
WebhookSubscription { Id, MerchantId, OrganizationId, TargetUrl, Secret (hashed at
                        rest), Events[], IsActive }

WebhookDelivery { Id, WebhookSubscriptionId, EventType, Payload, Attempt,
                    ResponseStatus, ResponseSnippet, DeliveredAt (nullable),
                    NextRetryAt (nullable) }
```

## Audit — `OrganizationId` present (nullable for platform/cross-tenant rows)

```text
AuditLog { Id, OrganizationId (nullable) [C#5], UserId, Action, EntityType, EntityId,
            Before (json), After (json), IpAddress, UserAgent, Timestamp,
            IsCrossTenantAction (bool, default false) [C#5] }
```

---

## Relationship Summary (high level)

```text
Organization 1─* Branch
Organization 1─* User ─* UserRole *─1 Role *─* Permission
User 1─* UserBranch *─1 Branch
Organization 1─* Merchant 1─* MerchantUser
Merchant 1─* ApiKey
Organization 1─* RateCard 1─* ZoneRate
Merchant 1─* Shipment *─1 Branch (origin/destination/current)
Shipment 1─* ShipmentItem
Shipment 1─* ShipmentEvent
Shipment 1─* DeliveryAttempt 1─(0..1)─ DeliveryProof
Shipment 1─(0..1)─ NDR 1─(0..1)─ ReturnShipment
Shipment 1─(0..1)─ CODCollection 1─(0..1)─ MerchantSettlementItem *─1 MerchantSettlement
Manifest 1─* ManifestShipment *─1 Shipment
Rider 1─* DeliveryTask *─1 Shipment
```

This summary is illustrative — the field lists above each entity are the actual
contract; migrations should be generated from those, and any divergence discovered
during implementation gets fixed here first (per `ROADMAP.md`'s cross-phase rule),
not left implicit in code.
