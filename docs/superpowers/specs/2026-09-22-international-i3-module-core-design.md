# International I3 — International Module Core

Parent: `2026-09-21-international-support-design.md` (§5 International module, §8 step I3). Follows
I0 (`Money`/`CurrencyCode`, tenant locale), I1a/I1b (i18n foundation, notification locale) and I2
(`Country`/`CountryZone`/`InternationalAddress`).

## 1. Goal

Give `Shipment` the ability to represent an international parcel (`Scope`, `Direction`, mixed
Nepal/foreign addressing), introduce the `International` module with a one-to-one
`InternationalLeg` (customs declaration + provisional pricing placeholder), and let staff create
and progress an international shipment shell through customs preparation — without yet touching
carriers, real pricing, or any customer-facing surface. Those are I4 (carrier layer), I5
(pricing), I6 (DHL), I7 (frontend/merchant API), I8 (reports).

## 2. Decisions

- **Module placement.** New `International` module (`src/Modules/International/{Domain,Application,Infrastructure,Presentation}`),
  structured like every other module. `Shipment` stays the single aggregate (parent spec's
  rejected alternatives: extending `Shipment` in place couples domestic/international rules; a
  separate `InternationalShipment` aggregate duplicates tracking/custody/scanning/COD
  hooks/reports/merchant API). `International` never touches the `Shipment`/`ShipmentEvent`
  tables directly — it goes through a new `IShipmentInternationalGateway` interface, implemented
  by `Shipments.Application.ShipmentService`, following the exact precedent of the existing
  `IShipmentTransitionService` (used today by `Manifests`/`Ndr`/`Riders` to drive real shipment
  transitions without touching `Shipment`'s `DbSet`).
- **Branch semantics.** `OriginBranchId` = where the shipment enters our domestic custody chain
  (pickup branch for Domestic/Outbound; the import/gateway branch for Inbound). `DestinationBranchId`
  = the branch responsible for the final leg out of our network (delivery branch for
  Domestic/Inbound; the export/gateway branch for Outbound). No new branch fields are needed —
  this reframing of the existing two fields is symmetric and keeps the existing
  branch-transfer/manifest machinery meaningful for both directions.
- **`CustomsPrepared` reachability (resolves an asymmetry the parent spec's compressed diagrams
  leave implicit).** `Manifests.Application.ManifestService.CreateAsync` hard-rejects
  `FromBranchId == ToBranchId` — there is no manifest path from `Sorted` to `AtDestinationBranch`
  when a shipment's origin branch is also its own gateway branch (the common case: a branch that
  handles exports locally). That is exactly what the parent spec's outbound diagram shows
  (`SORTED → CUSTOMS_PREPARED`, no hop in between). So `CustomsPrepared` is reachable from
  `Sorted` when `OriginBranchId == DestinationBranchId`, or from `AtDestinationBranch` when they
  differ (after the existing, unmodified manifest dispatch/receive cycle). This is the only new
  transition I3 wires into the gateway.
- **Status/event scope.** Only one new `ShipmentStatus`/`ShipmentEventType` value is added:
  `CustomsPrepared`. The parent spec's remaining new statuses (`HandedToCarrier`,
  `InInternationalTransit`, `DeliveredAbroad`, `CustomsHold`, `InternationalException` outbound;
  `InInternationalTransit`, `CustomsClearance`, `ReceivedFromCarrier` inbound) are deliberately
  **not** added yet — every prior phase has grown `ShipmentStatus`/`ShipmentEventType` in lockstep
  with the code that uses the new values, never pre-declaring dormant ones, and I4 is what
  actually implements the carrier scans that reach them. Adding them now would sit unreachable for
  an unknown number of sprints, and the append-only-ordinal rule (`CLARIFICATIONS.md` #10) means
  nothing is lost by waiting — I4 just appends its own values at the end when it needs them.
- **Pricing.** I3 does **not** build `PriceBreakdown` (I5) or touch `Pricing`/`FxRate` beyond what
  I0 already ships. `InternationalLeg` instead carries a staff-entered `ProvisionalTotalCharge`
  (`Money`) and `IsProvisionalPricing = true`, surfaced in every response so nothing pretends this
  is a real carrier-cost-based price. I5 replaces it with a real breakdown; this field is not
  migrated forward automatically — I5's own design decides how existing I3-era provisional
  shipments are reconciled.
- **Creation surface.** A new `POST /api/v1/international/shipments` in `International.Presentation`
  — not an extension of the existing `POST /api/v1/shipments` — keeps domestic and international
  creation rules decoupled, consistent with the parent spec's rejected in-place-extension
  alternative. Staff-only (`international.create`); merchant-facing booking is I7. This makes I3
  demoable/testable end-to-end (per the resolved I3-scope question) instead of being pure
  unreachable plumbing, and gives I5 a real shipment to price against instead of designing pricing
  blind.
- **`CarrierBooking`/`DutiesAndTaxes` deferred.** These `InternationalLeg` sub-entities from the
  parent spec's §5.2 are not created in I3 — no carrier integration exists yet (I4/I5). Adding
  empty tables now would be schema churn ahead of the design that actually fills them.

## 3. Data model

**`Shipment`** (`Shipments.Domain`) gains:

| Field | Type | Notes |
|---|---|---|
| `Scope` | `ShipmentScope` enum (`Domestic`, `International`) | Default `Domestic`; set at creation, immutable |
| `Direction` | `ShipmentDirection` enum (`Domestic`, `Outbound`, `Inbound`) | Default `Domestic`; set at creation, immutable |

`RecipientProvinceId`/`RecipientDistrictId`/`RecipientLocalLevelId`/`RecipientWardNumber` and the
equivalent `Sender*` fields become nullable (`Guid?`/`int?`). New nullable columns:
`RecipientAddressLine2`, `RecipientCity`, `RecipientRegion`, `RecipientPostalCode`,
`RecipientCountryCode`, and the equivalent `Sender*International*` set — populated from I2's
`InternationalAddress` value object at the application boundary (parsed via
`InternationalAddress.Create`, persisted as plain columns the same way `Country.DefaultCurrency`
stores a parsed-but-plain `CurrencyCode`). `RecipientAddressLine`/`SenderAddressLine` (existing
`string` columns) are reused as `Line1` for whichever side is foreign, avoiding a duplicate
`Line1` column.

Domain validation (in `Shipment`'s constructor path / a new `ShipmentAddressValidator`) enforces:
domestic shipments require both full Nepal chains unchanged (existing behavior, untouched);
international shipments require the Nepal chain on the domestic-side party and the
`InternationalAddress` fields on the foreign-side party, where the foreign side is determined by
`Direction` (`Outbound` → recipient is foreign; `Inbound` → sender is foreign).

**`ShipmentStatus`/`ShipmentEventType`** (`Shipments.Domain`): append `CustomsPrepared` at the end
of both enums (ordinal-append rule, `CLARIFICATIONS.md` #10).

**`InternationalLeg`** (new `International.Domain`, one-to-one by `ShipmentId`, tenant-scoped via
`OrganizationId` + the standard query filter — unlike `FxRate`/`Country`, this is shipment-owned
tenant data, not platform reference data):

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `OrganizationId` | `Guid` | Standard tenant query filter |
| `ShipmentId` | `Guid` | Unique FK to `Shipment` |
| `ContentsDescription` | `string` | Required once customs is completed |
| `CountryOfOrigin` | `string(2)` | ISO alpha-2, required |
| `Purpose` | `CustomsPurpose` enum (`Gift`, `CommercialSale`, `Sample`, `Return`, `Documents`, `Other`) | |
| `Incoterm` | `Incoterm` enum (`Ddu`, `Ddp`) | |
| `ExportLicenceReference` | `string?` | |
| `CustomsCompletedAt` | `DateTimeOffset?` | Null until the declaration is complete; gates `CustomsPrepared` |
| `ProvisionalTotalChargeAmount` | `decimal` | Paired with `ProvisionalTotalChargeCurrency` to form a `Money` |
| `ProvisionalTotalChargeCurrency` | `string(3)` | |
| `IsProvisionalPricing` | `bool` | Always `true` in I3 |
| `CreatedAt`/`UpdatedAt` | `DateTimeOffset` | |

**`CustomsItem`** (new `International.Domain`, child of `InternationalLeg`, one row per
`ShipmentItem`): `Id`, `InternationalLegId`, `ShipmentItemId` (FK), `HsCode` (`string`, required),
`CustomsValueAmount`/`CustomsValueCurrency` (→ `Money`).

**Migrations.** Three additive migrations, matching the granularity precedent set by I0/I1b/I2:
1. `AddShipmentInternationalFields` — `Scope`, `Direction` (defaulted `Domestic` for backfill),
   nullable Nepal-geography columns, new `InternationalAddress` columns, `CustomsPrepared` enum
   values (no schema change needed for the enum itself — it's stored as an `int`).
2. `AddInternationalLeg` — creates `InternationalLegs` and `CustomsItems`.
3. `AddInternationalPermissions` — inserts the five new `Permissions` rows (see §5), following the
   exact `AddRiderLocationReadPermission` shape.

## 4. Application layer

**`Shipments.Application`** gains `IShipmentInternationalGateway`:

```csharp
public interface IShipmentInternationalGateway
{
    Task<ShipmentResponse> CreateShipmentShellAsync(CreateInternationalShipmentShellRequest request, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
    Task<ShipmentResponse?> TransitionToCustomsPreparedAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken cancellationToken = default);
}
```

Implemented by `ShipmentService` (a second interface alongside `IShipmentTransitionService`).
`CreateShipmentShellAsync` reuses `CreateShipmentAsync`'s AWB-generation/retry-on-collision loop
and audit/outbox calls, branching only on address validation (Nepal chain vs
`InternationalAddress` per `Direction`) and skipping `PricingService.QuoteAsync`/COD entirely for
`Scope = International` (COD guard: `request.CodAmount > 0` throws `ShipmentValidationException`
when `Scope = International`). `TransitionToCustomsPreparedAsync` reuses the existing
`TransitionAsync` private helper from `ShipmentService.Transitions.cs`, with `expectedFrom` chosen
dynamically (`Sorted` when `OriginBranchId == DestinationBranchId`, else `AtDestinationBranch`) —
the one deliberate deviation from every other transition method's single hardcoded `expectedFrom`.

**`Geography.Application`** gains `ICountryLookupService` (same cross-module-lookup shape as the
existing `IGeographyLookupService`/`IBranchLookupService`): `Task<bool> IsServicedAsync(string isoCode, CancellationToken)`,
implemented against `Country` (`IsoCode` match, `IsServiced == true`). This is the parent spec §9
enforcement point ("`Country.IsServiced` enforced server-side for sanctioned/unserviced
destinations") — I2 built `Country`/`IsServiced` and the admin screen to toggle it, but nothing
consumed it yet ("no consumer" was I2's own stated known limitation). I3 is the first consumer.

**New `International.Application`:**
- `InternationalShipmentService.CreateAsync` — validates the foreign-side `InternationalAddress.CountryCode`
  against `ICountryLookupService.IsServicedAsync` first (400 `INTERNATIONAL_SHIPMENT_VALIDATION_FAILED`,
  message "Destination country is not currently serviced." if false or unknown), then calls
  `IShipmentInternationalGateway.CreateShipmentShellAsync`, then creates `InternationalLeg`
  (customs fields empty, `ProvisionalTotalChargeAmount` from the request, `IsProvisionalPricing = true`)
  in the same `DbContext` instance (both modules share one `DbContext` in this modular monolith, so
  both writes commit in one `SaveChangesAsync`/transaction).
- `InternationalShipmentService.UpdateCustomsAsync` — validates and persists `CustomsDeclaration`
  fields plus per-item `CustomsItem` rows (one per the shipment's existing `ShipmentItem`s, looked
  up via a new narrow `IShipmentItemLookupService` read-only projection — `International` does not
  query `Shipment`'s tables directly, matching the "never touches shipment tables" rule), sets
  `CustomsCompletedAt`, audits `international_leg.customs_updated`.
- `InternationalShipmentService.PrepareCustomsAsync` — validates `CustomsCompletedAt` is set, then
  calls `IShipmentInternationalGateway.TransitionToCustomsPreparedAsync`.
- `InternationalShipmentQueryService.GetAsync` — merges `Shipment` (via a narrow read projection,
  same pattern as `ShipmentScanInfo`) with `InternationalLeg`/`CustomsItem` for the detail response.

## 5. API

| Route | Auth | Notes |
|---|---|---|
| `POST /api/v1/international/shipments` | `international.create`, branch-scoped to `OriginBranchId` (same `CheckBranchAccessAsync` pattern as existing shipment creation) | Body: `direction`, `originBranchId`, `destinationBranchId`, domestic-side Nepal address, foreign-side `InternationalAddress` fields, items, `provisionalTotalCharge` (amount + currency). 400 `INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` on address-shape/COD-guard violations. |
| `GET /api/v1/international/shipments/{id}` | `international.read`, branch-scoped | Merged `Shipment` + `InternationalLeg` + `CustomsItem` detail |
| `PATCH /api/v1/international/shipments/{id}/customs` | `international.manage_customs`, branch-scoped | Full-replace body (contents, HS codes/customs values per item, country of origin, purpose, incoterm, licence ref) |
| `POST /api/v1/international/shipments/{id}/prepare-customs` | `international.manage_customs`, branch-scoped | Triggers the `CustomsPrepared` transition; 409 `SHIPMENT_INVALID_TRANSITION` if the state guard fails, 400 if customs isn't complete |

All four require an active organization context and enforce tenant isolation the same way every
other `Shipments`/`Manifests` endpoint does (cross-tenant ID manipulation returns 404, not 403,
per existing precedent).

## 6. Permissions & audit

`PermissionCatalog` gains (own migration): `international.create`, `international.read`,
`international.manage_customs`, `international.handoff`, `international.receive` — the last two
seeded now but unused until I4 wires the carrier handoff/receive scans that consume them.
`AdditionalRoleSeeds` assigns `international.create`/`international.read`/`international.manage_customs`
to `OperationsManager` and `HubStaff` (the two existing roles whose descriptions already cover
outbound/inbound branch operations); no other default role changes.

Every write is audited via the existing `IAuditLogger`: `international_shipment.created`,
`international_leg.customs_updated`, `shipment.customs_prepared` (the transition itself, matching
the `shipment.sorted`/`shipment.received_at_origin` naming convention already used by
`ShipmentService.Transitions.cs`).

## 7. Guards

- COD rejected on `Scope = International` at shell creation (`CodAmount` isn't even a field on the
  international creation request — there is no way to set it, which is the enforcement).
- Foreign-side `CountryCode` rejected at shell creation unless `Country.IsServiced` (via the new
  `ICountryLookupService`) — parent spec §9.
- **The guard is on `Direction`, not `Scope`, and applies to exactly one cluster of methods.**
  `PickupAssigned`/`PickedUp`/`AtOriginBranch`/`Sorted`/dispatch/`InTransit`/receive-at-destination
  are shared statuses that both `Outbound` and (once I4 ships `ReceivedFromCarrier`) `Inbound`
  international shipments legitimately pass through unchanged — no guard needed on those methods.
  The methods that must reject `Direction = Outbound` are exactly the ones that would otherwise
  let an outbound international shipment skip `CustomsPrepared` straight into domestic last-mile
  terminal statuses: `AssignDeliveryToRiderAsync`, `CompleteDeliveryAsync`, `FailDeliveryAsync`,
  `TransitionToNdrCreatedAsync`, `ReattemptDeliveryAsync`, `InitiateRtoAsync`,
  `DispatchManyRtoAsync`, `ReceiveRtoAtOriginAsync`, `ConfirmRtoDeliveredAsync` — each gains a
  leading `if (shipment.Direction == ShipmentDirection.Outbound) throw ShipmentInvalidTransitionException`.
  `Inbound` is deliberately **not** guarded here: an inbound shipment's last mile (from
  `AtDestinationBranch` onward, once I4 lets it reach that status via `ReceivedFromCarrier`) is
  supposed to use these exact domestic methods unchanged. In I3, no inbound shipment can reach
  `AtDestinationBranch` yet (I4 hasn't shipped `ReceivedFromCarrier`), so this is currently
  unreachable for `Inbound` — the guard is written in terms of `Direction == Outbound` now so it
  stays correct once I4 ships, rather than needing to be revisited then.
- `TransitionToCustomsPreparedAsync` rejects `Scope = Domestic` shipments and rejects
  `CustomsCompletedAt == null`.

## 8. Testing

- **Unit:** `Shipment` address validation (Nepal chain vs `InternationalAddress` per `Direction`,
  both directions, both valid and invalid shapes); `CustomsPrepared` reachability from `Sorted`
  (same-branch) and from `AtDestinationBranch` (different-branch), and rejection from every other
  status; COD-on-international guard; domestic-transition-rejected-on-international guard.
- **Integration (DB):** all three migrations apply cleanly; `CreateShipmentShellAsync` persists a
  `Shipment` row without `International.Application` ever calling `_dbContext.Set<Shipment>()`
  directly (verified by code review / a namespace-boundary test, not runtime); `InternationalLeg`
  tenant isolation (cross-org shipment ID returns 404); the existing `ManifestService.CreateAsync`
  same-branch rejection is unaffected by the branch-semantics reframing.
- **API:** `international.*` permission gating on all four routes (403/404 shape matching existing
  precedent); validation error codes; the same-branch vs different-branch `CustomsPrepared` path
  exercised end-to-end through both routes.

## 9. Docs to update

`INTERNATIONAL.md` (new I3 section, status table row), `DOMAIN-MODEL.md` (`Shipment.Scope`/`Direction`,
`InternationalLeg`, `CustomsItem`), `SHIPMENT-LIFECYCLE.md` (the `CustomsPrepared` branch and its
same-branch/different-branch reachability rule), `RBAC.md` (five new permissions, role
assignments), `API-SPEC.md` (four new routes).

## 10. Out of scope

Carrier port/adapters, `CarrierHandoff`/`CarrierReceive` scans, tracking ingest, `CarrierBooking`/
`DutiesAndTaxes` entities, real `PriceBreakdown`/carrier-quote pricing, merchant-facing API,
frontend, webhooks, reports, the remaining seven `ShipmentStatus`/`ShipmentEventType` values from
the parent spec's §5.1 diagrams (all I4).
