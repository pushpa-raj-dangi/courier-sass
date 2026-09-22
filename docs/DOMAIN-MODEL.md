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
         ParentBranchId (nullable, self-FK), DeliveryZoneId (nullable FK into
         Geography.DeliveryZone, ops-assigned — Phase 5), IsActive }
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
         Merchant.OrganizationId), HashedKey (SHA-256 of the secret half of
         "{Prefix}.{Secret}"; plaintext shown once, at creation), Prefix (8 hex
         chars, unique, unhashed — O(1) lookup before the secret is hashed and
         compared), Scopes[], IsActive, CreatedAt, RevokedAt (nullable),
         LastUsedAt (nullable) }                                     [C#7] [C#13]
         # MerchantId's FK was missing from the original plan (C#13); shipped as
         # builder.HasOne<Merchant>().WithMany().HasForeignKey(k => k.MerchantId)
         # .OnDelete(Restrict), matching every other Merchants child entity's
         # own local FK-declaration convention (MerchantKycDocument,
         # MerchantAddress, MerchantUser).
```

## Pricing — `OrganizationId` present (except shared `ServiceType`/`WeightSlab` templates)

```text
ServiceType { Id, Code (Standard|Express|SameDay|Heavy|Special), Name }
WeightSlab { Id, MinWeightKg, MaxWeightKg (nullable — open-ended top slab, e.g. "10kg+") }
RateCard { Id, OrganizationId, Name, EffectiveFrom, EffectiveTo (nullable),
           Version, IsActive }
ZoneRate { Id, RateCardId, OriginZoneId, DestinationZoneId (both FK straight into
           Geography.DeliveryZone — Pricing has no separate Zone entity; DeliveryZone
           already has the identical OrganizationId+Name+CoveredAreas shape),
           ServiceTypeId, WeightSlabId, BaseRate }
AdditionalCharge { Id, RateCardId, Type (Insurance|COD|Fuel|Other), CalculationType
                    (Flat|Percentage), Value }
MerchantRateCard { Id, MerchantId, OrganizationId, RateCardId,
                    OverrideDiscountPercent (nullable) }
```

`RateCard` is immutable once created — a rate change is a new `RateCard` version with a new
`EffectiveFrom` (`Version` incremented, previous row's `IsActive` flips to `false` and
`EffectiveTo` is set), never an in-place edit (CLAUDE.md §16). `ZoneRate`/`AdditionalCharge` rows
can only be added while their parent `RateCard` is still the org's active version; a superseded
`RateCard`'s children are frozen forever. No `RateCardRule` entity exists — the pricing engine
queries `ZoneRate`/`AdditionalCharge` directly by `RateCardId`, since both already carry it and no
extra evaluable rule logic is needed beyond that direct lookup (ROADMAP.md Phase 5).

`Branch` (see the Branches section above) gains a `DeliveryZoneId` (nullable FK into
`Geography.DeliveryZone`) as part of Phase 5 — ops-assigned, used to resolve a shipment's
origin/destination branch to a rating zone for `ZoneRate` lookup.

## Shipments & Tracking — `OrganizationId` present

```text
Shipment { Id (internal UUID), Awb (business identifier, unique per (OrganizationId, Awb),
             format {OrgSlugPrefix}{yyyyMMdd}{6-digit sequence}, e.g. ACME20260820000001),
           OrganizationId, MerchantId,
           OriginBranchId, DestinationBranchId, CurrentBranchId (nullable),
           CurrentCustodianUserId (nullable), CurrentCustodianRiderId (nullable — always
             null until Phase 8 introduces Rider; kept as a real column now so Phase 8
             needs no migration to add it),
           CurrentStatus (Created|PickupAssigned|PickedUp|AtOriginBranch|Sorted|InTransit|
             AtDestinationBranch|OutForDelivery|Delivered|Cancelled — NDR/RTO states added
             Phase 9), ServiceTypeId,
           WeightKg, LengthCm (nullable), WidthCm (nullable), HeightCm (nullable),
           DeclaredValue (= sum of ShipmentItem.Quantity*UnitValue, server-computed),
           CodAmount, WantsInsurance,
           DeliveryCharge, InsuranceCharge, OtherCharges, TotalCharge,
             # ^ all snapshotted from PricingService.QuoteAsync at booking time and
             # never recalculated — DeliveryCharge+InsuranceCharge+OtherCharges equal the
             # pre-discount Subtotal; TotalCharge is the actual post-discount amount, so the
             # three charge fields will not sum to TotalCharge when a merchant discount
             # applied (expected, not a bug — every component stays individually traceable
             # per CLAUDE.md §23)
           RecipientName, RecipientPhone, RecipientProvinceId, RecipientDistrictId,
             RecipientLocalLevelId, RecipientWardNumber, RecipientAddressLine,
           SenderName, SenderPhone, SenderProvinceId, SenderDistrictId, SenderLocalLevelId,
             SenderWardNumber, SenderAddressLine }

ShipmentItem { Id, ShipmentId, Description, Quantity, UnitValue }

ShipmentEvent { Id, ShipmentId, EventType (Created|PickupAssigned|PickedUp|
                ReceivedAtOrigin|Sorted|Dispatched|ReceivedAtDestination|OutForDelivery|
                Delivered|Cancelled), PreviousStatus (nullable — null for the Created
                event), NewStatus, BranchId (nullable), UserId (nullable), RiderId
                (nullable — always null this phase), Timestamp, Latitude (nullable),
                Longitude (nullable), Notes (nullable), Metadata (nullable) }
                # append-only, never updated/deleted

PickupRequest { Id, OrganizationId, MerchantId, BranchId (resolved server-side from
                the requested shipments' shared OriginBranchId at creation — every
                ShipmentId in one request must share one OriginBranchId; a validated
                denormalization, never caller-supplied), ShipmentIds (Guid[]),
                RequestedForDate (DateOnly), Notes (nullable),
                Status (Requested|Scheduled|Completed|Cancelled), CreatedAt,
                UpdatedAt }
                # Phase 12. No rider/DeliveryRun linkage yet (out of this phase's
                # scope) — Status only ever reaches Cancelled via the merchant this
                # phase; Scheduled/Completed exist as columns now so no migration is
                # needed once the Phase 8 DeliveryRun wiring is extended to pickup
                # requests, but nothing transitions a row into them yet.
```

**Custody note:** `CurrentCustodianId` is polymorphic (a branch-based staff user or a
rider) in practice modeled as two nullable FKs (`CurrentCustodianUserId`,
`CurrentCustodianRiderId`) with exactly one non-null at a time, rather than a true
polymorphic FK — kept explicit for referential integrity rather than using a
type+id pair.

**RecipientAddress/SenderAddress** are structured geography FKs (Province/District/
LocalLevel/WardNumber + AddressLine), the identical shape `Branch`/`MerchantAddress` already
use — not the single free-text field an earlier draft of this section implied.

## Scanning & Manifests — `OrganizationId` present

```text
Manifest { Id, OrganizationId, Code (e.g. MNF-20260818-001, unique per (OrganizationId, Code)),
           FromBranchId, ToBranchId, VehiclePlate (nullable until dispatch), DriverId (nullable —
           no FK yet, Rider doesn't exist until Phase 8), Status (Draft|Sealed|InTransit|Received|
           Closed — no separate "Dispatched" resting state; POST /manifests/{id}/dispatch moves
           Sealed straight to InTransit in one action, recording VehiclePlate/DriverId at the same
           time, since nothing distinguishes a dispatched-but-not-yet-in-transit state in practice),
           ShipmentCount, CreatedByUserId, CreatedAt }

ManifestShipment { Id, ManifestId, ShipmentId, ScannedAt, ReceivedAt (nullable — a null value on a
                    Closed manifest IS the missing-shipment flag; no separate IsMissing column) }

ShipmentTransfer { Id, ManifestId, ShipmentId, FromBranchId, ToBranchId, Timestamp }
             # append-only physical-custody record, one row per shipment at receive-time,
             # separate from Shipments.ShipmentEvent (SHIPMENT-LIFECYCLE.md §5)
```

## Riders & Delivery — `OrganizationId` present on Rider/Vehicle/DeliveryAttempt/DeliveryProof only

```text
Rider { Id, OrganizationId, UserId (FK — a rider is also a User with the
        PickupRider/DeliveryRider role), BaseBranchId, LicenseNumber, RoleType, IsActive }
        # No RiderAssignment entity (Phase 8's resolved design decision) — BaseBranchId is the
        # sole branch scope.

Vehicle { Id, OrganizationId, RiderId (nullable), Type, PlateNumber, Capacity }

DeliveryRun { Id, RiderId, BranchId, Date, Status (Active|Completed) }
             # No own OrganizationId — flows transitively through RiderId -> Rider.OrganizationId.
             # Auto-created/reused per (RiderId, BranchId, Date) when the first task of the day is
             # assigned; no explicit "start my run" endpoint (Phase 8).

DeliveryTask { Id, DeliveryRunId, ShipmentId, Type (Pickup|Delivery), Sequence,
               Status (Pending|Completed|Failed) }
             # No own OrganizationId — flows transitively through DeliveryRunId -> DeliveryRun ->
             # RiderId -> Rider.OrganizationId.

DeliveryAttempt { Id, OrganizationId, ShipmentId, RiderId, AttemptNumber, AttemptedAt, Succeeded,
                   Reason (CustomerUnavailable|PhoneUnreachable|WrongAddress|
                   IncompleteAddress|CustomerRefused|CustomerRequestedReschedule|
                   CashUnavailable|DamagedParcel|Other — null iff Succeeded),
                   Notes, Latitude, Longitude, CustomerContacted, NextAction (plain string) }
                 # Succeeded is additive beyond this document's original sketch — one row per real
                 # attempt (success or failure), disambiguated by this flag rather than a
                 # nullable-Reason convention (Phase 8).

DeliveryProof { Id, OrganizationId, ShipmentId, DeliveryAttemptId, RiderId, OtpVerified,
                SignatureStorageKey, PhotoStorageKey, Latitude, Longitude, Timestamp,
                RecipientName, CorrectedAt (nullable), CorrectionReason (nullable) }
                # Immutable after successful capture except via the correction fields above, which
                # never overwrite the original capture. OtpVerified is a client-reported boolean —
                # no server-side OTP generate/verify flow exists yet (Phase 8; needs Phase 11's real
                # SMS delivery to be meaningful). No correction endpoint exists yet either — the
                # columns exist now so a later phase adding one needs no migration.

RiderLocationPing { Id, RiderId, DeliveryRunId (nullable), Latitude, Longitude,
                     AccuracyMeters (nullable), RecordedAt, ReceivedAt }
                   # No own OrganizationId — flows transitively through RiderId -> Rider.OrganizationId,
                   # the same pattern DeliveryTask establishes above (via DeliveryRunId -> DeliveryRun ->
                   # RiderId -> Rider.OrganizationId). DeliveryRunId is nullable: a rider can report a
                   # position before their first task of the day is assigned. RecordedAt is the rider
                   # device's own client-reported timestamp (trusted only for chronological ordering
                   # within a run's breadcrumb); ReceivedAt is set server-side (DateTimeOffset.UtcNow at
                   # insert) and is what "last seen" age display and the retention job's 30-day pruning
                   # are anchored to, so a skewed device clock can neither fake freshness nor corrupt
                   # what gets deleted (Phase 14a).
```

## NDR — `OrganizationId` present

```text
NDR { Id, OrganizationId, ShipmentId, Reason (Ndr.Domain.NdrReason — a fresh copy of
      Riders.Domain.DeliveryAttemptReason's 9 values, not a shared reference; see
      CLARIFICATIONS.md #9), AssignedStaffUserId (nullable — set to whichever staff user first
      calls /update), Status (Open|Contacting|CustomerConfirmed|
      ReattemptScheduled|RtoRequested|Resolved), CustomerResponse, FollowUpAt,
      NextAction, Resolution (Reattempt|Rto|AutoRto|Other), ResolvedAt }         [C#4] [C#9]
      # Entity name is the all-caps NDR, not Ndr — deliberate, to avoid a CS0118
      # ('NDR' is a namespace but is used like a type) collision with the Courier.Ndr namespace.
      # Auto-created by Ndr.Application.NdrCreationService.CreateFromFailedAttemptAsync,
      # called from Riders.Application.RiderService.RecordFailedAttemptAsync immediately after
      # FailDeliveryAsync, in the same request (Phase 9) — DELIVERY_FAILED -> NDR_CREATED is a
      # system step, never a separate manual action.
```

## RTO — `OrganizationId` present

```text
ReturnShipment { Id, OriginalShipmentId, OrganizationId, ReturnAwb,
                  RtoReason, OriginBranchId, DestinationBranchId,
                  MerchantConfirmedAt (nullable) }         [C#9]
                  # No Status/CurrentBranchId/ReceivedAt columns (unlike this section's original
                  # sketch) — both are read live from the underlying Shipment (via
                  # OriginalShipmentId) instead of cached here, CLARIFICATIONS.md #9. OriginBranchId
                  # is the ORIGINAL shipment's DestinationBranchId (the return leg starts where
                  # delivery was attempted); DestinationBranchId is the ORIGINAL shipment's
                  # OriginBranchId (the return leg ends where the parcel came from) — the swap
                  # happens once, at creation, in Ndr.Application.NdrService.ResolveAsync.
```

## COD & Finance — `OrganizationId` present (`MerchantSettlementItem` excepted — see below)

```text
CODCollection { Id, ShipmentId (unique), OrganizationId, MerchantId, BranchId, ExpectedAmount,
                 ShipmentTotalCharge, CollectedAmount (nullable),
                 Status (CodExpected|CodCollected|RiderDeposited|BranchReconciled|
                 FinanceVerified|MerchantSettlementCreated|Settled),         [C#11]
                 CollectedByRiderId (nullable), CollectedAt (nullable), DeliveryProofId (nullable),
                 RiderCashSettlementId (nullable), MerchantSettlementItemId (nullable) }
                 # Created eagerly at booking (CodExpected) by a new ICodCollectionCreationService,
                 # called from Shipments.Application.ShipmentService.CreateShipmentAsync — MerchantId/
                 # BranchId/ShipmentTotalCharge are snapshotted here, so Cod never reads back into
                 # Shipments. Status carries the full lifecycle chain (COD-LIFECYCLE.md §1), not just
                 # Expected/Collected. [C#11]

RiderCashSettlement { Id, OrganizationId, RiderId, BranchId, DeclaredAmount, ShipmentCount,
                        SettledAt, ConfirmedByUserId, BranchCashSettlementId (nullable) }         [C#11]

BranchCashSettlement { Id, OrganizationId, BranchId, ConfirmedAmount, ReconciledByUserId,
                         ReconciledAt }
                         # No RiderCashSettlementIds[] array (unlike this section's original
                         # sketch) — RiderCashSettlement.BranchCashSettlementId is the link. [C#11]

FinancialTransaction { Id, OrganizationId, Type (CodVerification), Amount, ReferenceEntityType,
                         ReferenceEntityId, VerifiedByUserId, VerifiedAt }
                         # (ReferenceEntityType, ReferenceEntityId) is a unique index — at most one
                         # verification per BranchCashSettlement, DB-enforced. [C#11]

MerchantSettlement { Id, OrganizationId, MerchantId, PeriodFrom, PeriodTo,
                       TotalPayable, Status (Created|Settled), SettledAt (nullable) }

MerchantSettlementItem { Id, MerchantSettlementId, ShipmentId (unique), CODAmount,
                           CourierFee, OtherDeductions, NetPayable }
                           # No own OrganizationId — flows transitively through
                           # MerchantSettlementId -> MerchantSettlement.OrganizationId. CourierFee is
                           # copied from the covering CODCollection.ShipmentTotalCharge, never
                           # recomputed from a current rate card (CLAUDE.md §16).

Adjustment { Id, OrganizationId, EntityType, EntityId, BeforeValue, AfterValue,
              Reason, RequestedByUserId, RequestedAt, ApprovedByUserId (nullable),
              ApprovedAt (nullable), Applied (bool) }
              # v1 only supports EntityType = "MerchantSettlementItem". Applied stays false until a
              # separate ApproveAdjustmentAsync call — approval is always required, no configurable
              # threshold exists yet. [C#11]
```

## Events & Outbox — `OrganizationId` present

```text
OutboxMessage { Id, OrganizationId, EventType, Payload, CreatedAt, PublishedAt
                 (nullable), Attempts }   # backs both Notifications and Webhooks
                                            # per ARCHITECTURE.md's outbox pattern.
                 # Owned by the Courier.Events module — written by IOutboxWriter.WriteAsync
                 # inside the same SaveChangesAsync as the business change that caused it. A
                 # Hangfire recurring job (the "outbox publisher") reads unpublished rows and
                 # publishes each, wrapped in an envelope ({EventId, OrganizationId, EventType,
                 # Data}), to a shared RabbitMQ topic exchange (courier.domain-events, routing
                 # key = EventType). Events has zero project reference to any other module —
                 # Payload is an opaque JSON string it never interprets. [C#12]
```

## Notifications — `OrganizationId` present

```text
NotificationTemplate { Id, OrganizationId, EventType, Channel (SMS|Email|WhatsApp|
                         Push), Locale, Body, IsActive }
                         # No rows are seeded anywhere — NotificationDispatchService falls back
                         # to a compiled-in default body when no active override row exists for
                         # an (OrganizationId, EventType, Channel, Locale) combination, which is
                         # every combination as of this phase. [C#12]

NotificationLog { Id, OrganizationId, EventType, Channel, RecipientAddress (nullable), Status
                    (Sent|Logged|Failed|NoRecipient), SentAt, ProviderResponse }
                    # RecipientAddress is nullable to accommodate NoRecipient (no
                    # Merchant.ContactEmail on file) — a departure from this section's original
                    # non-nullable sketch. Status's four values replace this section's original
                    # unenumerated Status field: only Email is a real channel this phase (real
                    # SMTP via MailKit); Sms/WhatsApp/Push always resolve to Logged, never
                    # Sent/Failed, since no real provider is wired to them yet. [C#12]
```

## Webhooks — `OrganizationId` present

```text
WebhookSubscription { Id, MerchantId, OrganizationId, TargetUrl, Secret (encrypted at rest, not
                        hashed — see CLARIFICATIONS.md #12 resolution 1), Events[], IsActive }

WebhookDelivery { Id, WebhookSubscriptionId, SourceEventId, EventType, Payload, Attempt,
                    ResponseStatus (nullable), ResponseSnippet (nullable), DeliveredAt (nullable),
                    NextRetryAt (nullable) }
                    # SourceEventId is the originating OutboxMessage.Id — a unique index on
                    # (WebhookSubscriptionId, SourceEventId) makes duplicate-delivery-row
                    # creation from a redelivered RabbitMQ message DB-impossible, not just
                    # checked-then-written. [C#12]
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
RateCard 1─* AdditionalCharge
Geography.DeliveryZone 1─* ZoneRate (as OriginZone, and separately as DestinationZone)
Branch *─1 Geography.DeliveryZone
Merchant 1─(0..1)─ MerchantRateCard *─1 RateCard
Merchant 1─* Shipment *─1 Branch (origin/destination/current)
Shipment *─1 Pricing.ServiceType
Shipment 1─* ShipmentItem
Shipment 1─* ShipmentEvent
Shipment 1─* DeliveryAttempt 1─(0..1)─ DeliveryProof
Shipment 1─(0..1)─ NDR 1─(0..1)─ ReturnShipment
Shipment 1─(0..1)─ CODCollection 1─(0..1)─ MerchantSettlementItem *─1 MerchantSettlement
Manifest 1─* ManifestShipment *─1 Shipment
Manifest 1─* ShipmentTransfer *─1 Shipment
Rider 1─* DeliveryTask *─1 Shipment
```

This summary is illustrative — the field lists above each entity are the actual
contract; migrations should be generated from those, and any divergence discovered
during implementation gets fixed here first (per `ROADMAP.md`'s cross-phase rule),
not left implicit in code.

## Money, FX and locale (international support, I0)

- `Money` / `CurrencyCode` (SharedKernel): immutable amount + currency, same-currency arithmetic only.
- `FxRate` (Pricing): platform-level, append-only conversion rate; not tenant-owned.
- `OrganizationSettings.DefaultLocale` / `CalendarPreference`: per-tenant UI locale and calendar.
- `NotificationTemplate.Locale` (I1b): per-locale org override, resolved with a four-step fallback
  — org locale override -> compiled locale default -> org en override -> compiled en default.
- `Country` (Geography, platform-level, I2): ISO-3166 alpha-2 reference data, `IsServiced` toggle,
  optional `CountryZone`. `InternationalAddress` (Geography, I2): format-validated foreign address
  value object, no consumer yet.
- Existing persisted amounts are NPR by invariant. See `docs/INTERNATIONAL.md`.
