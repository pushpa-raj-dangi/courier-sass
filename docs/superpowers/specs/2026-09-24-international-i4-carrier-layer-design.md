# International I4 — Carrier Layer

Parent: `2026-09-21-international-support-design.md` (§5.1 statuses, §5.4 custody, §5.5
authorization, §6 carrier integration, §8 step I4). Follows I3
(`2026-09-22-international-i3-module-core-design.md`): `Shipment.Scope`/`Direction`,
`InternationalLeg`/`CustomsItem`, `IShipmentInternationalGateway`, `CustomsPrepared`, the five
seeded `international.*` permissions.

## 1. Goal

Carry international shipments through the rest of their lifecycle with partner carriers:
book an outbound shipment with a carrier, hand it over (our custody ends), ingest the carrier's
tracking into the platform state machine until `DeliveredAbroad`; and for inbound shipments, link
the foreign carrier's booking, ingest tracking through customs clearance, and receive the parcel
at the gateway branch (our custody resumes) into the existing domestic last-mile flow.

I4 ships the complete, carrier-agnostic infrastructure — provider port, registry, tenant carrier
accounts with encrypted credentials, signed webhook ingest, Hangfire polling, HTTP resilience,
append-only tracking events, event processing — with one production adapter,
`ManualCarrierProvider`. A test-only fake HTTP carrier exercises the webhook, polling and
resilience paths for real so I6 (DHL Express) only plugs in an adapter.

Out of scope: DHL adapter (I6); rates/`GetRatesAsync`, pricing, duties and taxes, financial
transactions (I5); frontend, merchant API, merchant-facing webhook event types and notifications
(I7); international returns; reports (I8).

## 2. Decisions

| Area | Decision |
|---|---|
| Placement | Everything carrier-related lives in the `International` module. `Shipments` stays the sole owner of the state machine, custody fields and `ShipmentEvent`s; `International` drives it only through new `IShipmentInternationalGateway` methods. Rejected: a separate generic `Carriers` module (serves only the deferred Phase 14). |
| Scope of infrastructure | Full: webhook receiver, polling job and resilience pipeline are built and tested now against a fake HTTP carrier. |
| Carrier configuration | Tenant-owned `CarrierAccount` (several per carrier allowed); credentials and webhook secret encrypted with ASP.NET Data Protection. The manual carrier is an account with no credentials. |
| Outbound booking | Separate idempotent `book` step at `CustomsPrepared`, no status change; the handoff scan requires a `Booked` booking. |
| Inbound start | Staff link the foreign carrier's booking to a `Created` inbound shipment; carrier tracking events then drive it into `InInternationalTransit` and onwards. |
| Exceptions | `CustomsHold`/`InternationalException` exit back into the lifecycle via carrier events or audited staff overrides. No international returns. |
| Ingest | Store-then-process: every source (webhook, poll, manual entry) inserts deduplicated `CarrierTrackingEvent`s; one processor applies them through the state machine. |
| Status mapping | Owned in code by each provider (`MapStatus`), not a table — carrier vocabularies are fixed per carrier API. |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Microsoft's Polly v8 integration). New dependency, justified: first outbound HTTP integration requiring retry, circuit breaker and timeouts. |

## 3. Lifecycle

### 3.1 New statuses

Seven `ShipmentStatus` values and seven matching `ShipmentEventType` values, appended after
`CustomsPrepared` (append-only ordinal rule): `HandedToCarrier`, `InInternationalTransit`,
`DeliveredAbroad`, `CustomsHold`, `InternationalException`, `CustomsClearance`,
`ReceivedFromCarrier`. Every one is reachable only when `Scope == International`; the transition
guard rejects them on domestic shipments.

### 3.2 Outbound

| From | To | Trigger |
|---|---|---|
| `CustomsPrepared` | `HandedToCarrier` | Handoff scan (§5.3) |
| `HandedToCarrier` | `InInternationalTransit` | Carrier event |
| `InInternationalTransit` | `DeliveredAbroad` (terminal), `CustomsHold`, `InternationalException` | Carrier event |
| `CustomsHold` | `InInternationalTransit`, `InternationalException` | Carrier event or staff override |
| `InternationalException` | `InInternationalTransit`, `DeliveredAbroad` | Carrier event or staff override |

### 3.3 Inbound

| From | To | Trigger |
|---|---|---|
| `Created` | `InInternationalTransit` | Carrier event, only once an inbound booking is linked (§5.2) |
| `InInternationalTransit` | `CustomsClearance`, `CustomsHold`, `InternationalException` | Carrier event |
| `CustomsClearance` | `CustomsHold` | Carrier event |
| `CustomsHold` | `CustomsClearance`, `InternationalException` | Carrier event or staff override |
| `InternationalException` | `InInternationalTransit`, `CustomsClearance` | Carrier event or staff override |
| `InInternationalTransit`, `CustomsClearance` | `ReceivedFromCarrier` | Receive scan (§5.4) |

After `ReceivedFromCarrier`:
- If `OriginBranchId == DestinationBranchId`, the receive scan immediately records a second
  transition `ReceivedFromCarrier → AtDestinationBranch` (two immutable events, one scan).
- Otherwise the existing sort scan (`SortAsync`) additionally accepts `ReceivedFromCarrier` as its
  from-status for `Direction == Inbound` only; the normal `Sorted → manifest → InTransit →
  AtDestinationBranch` flow follows. `SortAsync`'s only caller, `ManifestService.ScanAsync`
  (which owns the sort operation's expected-status and branch checks), is updated to accept the
  same inbound from-status. No other existing transition method changes.

I3's `Direction == Outbound` guard on the nine last-mile/NDR/RTO methods is unchanged; inbound
shipments use those methods normally.

### 3.4 Staff overrides

`POST /international/shipments/{id}/status-override` (`international.tracking.manage`) accepts a
target status and a required non-empty reason, and is allowed only for the transitions marked
"staff override" above. It is audited with before/after status and reason; the `ShipmentEvent`
carries the staff user.

### 3.5 Carrier-driven events

Carrier-driven transitions record a `ShipmentEvent` with `UserId = null`, `Timestamp` = the
carrier's `OccurredAt`, and metadata `{ source: "Carrier", carrierAccountId, carrierAwb,
carrierEventId, carrierStatusCode, location }`. An event whose target equals the current status is
a no-op (processing outcome `Ignored`); any other invalid transition is rejected by the state
machine (outcome `Rejected`), so a stale or out-of-order event can never move a shipment backwards.

### 3.6 Custody

- **Handoff:** `CurrentBranchId`, `CurrentCustodianUserId`, `CurrentCustodianRiderId` are
  cleared; `CarrierBooking.CustodyStartedAt` is set; the event metadata records the carrier
  account and carrier AWB. While `HandedToCarrier` or later, the carrier is the custodian — derived
  from status plus the active booking; no new `Shipment` column (Shipments stays unaware of
  carrier accounts).
- **Receive:** `CurrentBranchId` = the receiving branch; `CurrentCustodianUserId` = the scanning
  user.

### 3.7 Public tracking

`PublicTrackingStatusMapper`: `HandedToCarrier`, `InInternationalTransit`, `CustomsClearance`,
`ReceivedFromCarrier`, `CustomsHold`, `InternationalException` → `InTransit`; `DeliveredAbroad` →
`Delivered`. Exceptions are not exposed publicly.

## 4. Data model

All new entities are tenant-owned: `OrganizationId` column, FK to `Organization`, standard tenant
query filter in `AppDbContext`. Migrations are additive only.

### 4.1 CarrierAccount

`Id`, `OrganizationId`, `CarrierCode` (string, e.g. `MANUAL`, `DHL_EXPRESS`; must be registered in
`CarrierProviderRegistry`), `DisplayName` (≤200), `IsActive`, `EncryptedCredentials` (nullable;
JSON protected with Data Protection, purpose string `International.CarrierAccount.Credentials`),
`EncryptedWebhookSecret` (nullable; purpose `International.CarrierAccount.WebhookSecret`),
`WebhookPublicId` (random 32-char URL-safe string, unique across the platform), `PollingEnabled`,
`CreatedAt`, `UpdatedAt`.

- Secrets are never returned by the API or logged; responses expose `hasCredentials`,
  `hasWebhookSecret` booleans.
- An account is never deleted once referenced by a booking; it can be deactivated. Inactive
  accounts cannot book or link; their webhook calls return 404; they are skipped by polling.
- `PollingEnabled` / webhook secret are only accepted when the provider's capabilities support
  them.

### 4.2 CarrierBooking

`Id`, `OrganizationId`, `InternationalLegId` (FK), `CarrierAccountId` (FK, restrict), `Direction`
(`Outbound | Inbound`), `CarrierAwb` (nullable until booked, ≤100), `ServiceLevel` (nullable,
≤100), `LabelStorageKey` (nullable; `IObjectStorage`), `Status` (`Pending | Booked | Failed |
Cancelled`), `FailureReason` (nullable, ≤1000), `RawResponse` (jsonb, nullable, known secret fields
masked), `IdempotencyKey` (≤200), `BookedAt`, `CancelledAt`, `CustodyStartedAt`, `CreatedAt`,
`UpdatedAt`.

Indexes:
- Partial unique on `InternationalLegId` where `Status IN (Pending, Booked)` — at most one live
  booking per leg; `Failed`/`Cancelled` rows remain as history.
- Unique on `(CarrierAccountId, CarrierAwb)` where `CarrierAwb IS NOT NULL AND Status = Booked` —
  lets ingest resolve an event to exactly one booking.
- Unique on `(OrganizationId, IdempotencyKey)`.

### 4.3 CarrierTrackingEvent (append-only)

`Id`, `OrganizationId`, `CarrierAccountId` (FK), `CarrierBookingId` (nullable FK, set when
matched), `CarrierAwb` (≤100), `CarrierEventId` (≤200), `CarrierStatusCode` (≤100), `Description`
(≤1000, nullable), `Location` (≤300, nullable), `OccurredAt`, `Source` (`Webhook | Poll |
Manual`), `RawPayload` (jsonb, nullable), `EnteredByUserId` (nullable; manual only), `ReceivedAt`,
`ProcessingStatus` (`Pending | Applied | Ignored | Rejected | Unmatched`), `ProcessingNote`
(nullable, ≤1000), `MappedShipmentStatus` (nullable), `ProcessedAt` (nullable).

- Unique on `(CarrierAccountId, CarrierEventId)` — deduplication. Manual entries get a generated
  `CarrierEventId` (`manual-<guid>`).
- Index on `(ProcessingStatus, OccurredAt)` for the processor.
- Only the processing columns (`CarrierBookingId`, `ProcessingStatus`, `ProcessingNote`,
  `MappedShipmentStatus`, `ProcessedAt`) change after insert.

### 4.4 Unchanged

`InternationalLeg`, `CustomsItem` unchanged (the booking points to the leg). `Shipment` gains only
the seven enum values.

## 5. Components

### 5.1 Carrier port

In `International.Application`, platform types only:

```csharp
public interface ICarrierProvider
{
    string CarrierCode { get; }
    CarrierCapabilities Capabilities { get; } // SupportsWebhooks, SupportsPolling, RequiresManualAwb, SupportsLabels
    Task<CarrierBookingResult> BookAsync(CarrierBookingRequest request, CarrierCredentials credentials, string idempotencyKey, CancellationToken ct);
    Task CancelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken ct);
    Task<CarrierLabel?> GetLabelAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken ct);
    Task<IReadOnlyList<CarrierTrackingUpdate>> GetTrackingAsync(string carrierAwb, CarrierCredentials credentials, CancellationToken ct);
    bool VerifyWebhook(IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> rawBody, string webhookSecret);
    IReadOnlyList<CarrierTrackingUpdate> ParseWebhook(ReadOnlySpan<byte> rawBody);
    ShipmentStatus? MapStatus(string carrierStatusCode, ShipmentDirection direction);
}
```

`CarrierTrackingUpdate(CarrierAwb, CarrierEventId, CarrierStatusCode, Description, Location,
OccurredAt, RawPayload)`. `CarrierProviderRegistry` resolves providers by `CarrierCode` (all
registered `ICarrierProvider`s from DI); an unknown code is a validation error. `GetRatesAsync`
is added in I5.

Credentials are decrypted only inside `CarrierAccountSecrets` (Infrastructure) immediately before a
provider call, and never placed in logs, exceptions or `RawResponse`.

### 5.2 ManualCarrierProvider

- `BookAsync` returns `Booked` with the staff-supplied `CarrierAwb` (`RequiresManualAwb = true`);
  no network call. `CancelAsync` no-op. No labels, webhooks or polling.
- Status vocabulary and mapping:

| Code | Outbound | Inbound |
|---|---|---|
| `IN_TRANSIT` | `InInternationalTransit` | `InInternationalTransit` |
| `IN_CUSTOMS` | — (ignored) | `CustomsClearance` |
| `CLEARED` | `InInternationalTransit` | `CustomsClearance` |
| `CUSTOMS_HOLD` | `CustomsHold` | `CustomsHold` |
| `EXCEPTION` | `InternationalException` | `InternationalException` |
| `DELIVERED` | `DeliveredAbroad` | — (ignored; inbound delivery is our last mile) |
| `ARRIVED` | — (ignored) | — (ignored; arrival is the receive scan) |

Codes outside the vocabulary are rejected at manual-entry time (400).

### 5.3 Outbound booking and handoff

`POST /api/v1/international/shipments/{id}/book` — `international.book`; `Idempotency-Key` header
required (existing idempotency middleware applies); body `{ carrierAccountId, serviceLevel?,
carrierAwb? }`.

1. Validate before any write or carrier call: shipment visible (tenant + branch scope, else 404),
   `Direction == Outbound`, status `CustomsPrepared`, customs complete, no live booking, account
   active and in-tenant, `carrierAwb` present iff the provider `RequiresManualAwb`.
2. Insert a `Pending` booking and commit.
3. Call `BookAsync` through the resilience pipeline.
4. Success → `Booked` (+ `CarrierAwb`, `ServiceLevel`, `BookedAt`; label stored via
   `IObjectStorage` when returned). Failure → `Failed` + `FailureReason`, response
   `502 CARRIER_BOOKING_FAILED` (circuit open → `503 CARRIER_UNAVAILABLE`); the shipment stays
   `CustomsPrepared` and a new booking may be attempted.

`POST .../booking/cancel` — `international.book`; allowed only while the booking is `Booked` and
the shipment has not been handed over. Calls `CancelAsync` first; on success marks `Cancelled`.

`POST .../handoff` — `international.handoff`; branch-scoped to the shipment's `CurrentBranchId`
(visible but out-of-scope → 400 `SHIPMENT_VALIDATION_FAILED`, matching scan precedent); requires
a `Booked` booking; transitions `CustomsPrepared → HandedToCarrier` via the gateway and applies
§3.6 custody.

### 5.4 Inbound link and receive

`POST .../inbound-booking` — `international.book`; body `{ carrierAccountId, carrierAwb,
serviceLevel? }`; only `Direction == Inbound` at status `Created` with no live booking. Creates a
`Booked` booking directly (no carrier call). If the account has `PollingEnabled`, enqueues an
immediate poll for this booking.

`POST .../receive` — `international.receive`; must be performed at the shipment's
`OriginBranchId` by a user with branch access to it; from `InInternationalTransit` or
`CustomsClearance`; applies §3.3 and §3.6.

### 5.5 Ingest

All sources insert `CarrierTrackingEvent`s with `ON CONFLICT (CarrierAccountId, CarrierEventId) DO
NOTHING`, then enqueue the processor for the affected bookings.

- **Webhook** — `POST /api/v1/carrier-webhooks/{webhookPublicId}`, anonymous,
  `RequireIpRateLimit()`. Resolves the account by `WebhookPublicId` through one narrow
  `IgnoreQueryFilters()` lookup (documented as deliberate, like `OutboxPublisherJob`); unknown or
  inactive account → 404; no webhook secret or provider without webhook support → 404. Verifies the
  signature with a constant-time comparison; failure → 401, logged, nothing stored. Parses, inserts
  (organization taken from the account, never the payload), returns 202. Body size capped at
  1 MB.
- **Polling** — Hangfire recurring job `carrier-tracking-poll` (every 15 minutes). For each
  `Booked` booking on an active, polling-enabled account whose shipment is not terminal
  (`DeliveredAbroad`, `Delivered`, `Cancelled`, or inbound past `ReceivedFromCarrier`): call
  `GetTrackingAsync`, insert events with `Source = Poll`. Bounded per run (max 50 bookings per
  account); an open circuit or error is logged and the booking skipped until the next run.
- **Manual** — `POST .../carrier-events` — `international.tracking.manage`; body `{ statusCode,
  occurredAt, location?, description? }`; the shipment must have a `Booked` booking; the code must
  be in that provider's vocabulary; `occurredAt` not in the future (≤5 min skew). Inserts with
  `Source = Manual`, `EnteredByUserId`, audited.

### 5.6 Processing

Hangfire recurring job `carrier-event-processor` (every 30 s) plus immediate enqueue after inserts.
Picks `Pending` events ordered by `OccurredAt` per account/AWB (batch of 200). For each:

1. Match the `Booked` booking by `(CarrierAccountId, CarrierAwb)`; none → `Unmatched` (kept; re-
   evaluated if a matching booking is later linked).
2. `MapStatus(code, booking.Direction)`; null → `Ignored` (shown on the timeline as
   informational).
3. Within a tenant scope for the event's `OrganizationId` (§5.7), call
   `IShipmentInternationalGateway.ApplyCarrierStatusAsync(shipmentId, targetStatus, occurredAt,
   CarrierEventReference)`. Already at target → `Ignored`; invalid transition → `Rejected` with the
   reason; success → `Applied`.

Processing of one event never blocks others; an unexpected exception leaves the event `Pending`
with a note and is retried next run (max 5 attempts, then `Rejected`).

### 5.7 Background tenant scope

`ICurrentTenantProvider` today resolves only from `HttpContext`. Add a scoped
`TenantScopeOverride` (SharedKernel) that `HttpContextCurrentTenantProvider` consults first, set by
a `ITenantScopeFactory.BeginScope(organizationId)` used only by background jobs that must run
tenant-filtered application services. The processor and poller create one DI scope per
organization. No request path sets it.

### 5.8 Gateway additions (`IShipmentInternationalGateway`)

- `HandToCarrierAsync(shipmentId, actingUser, carrierEventReference, ct)` — branch check,
  transition, custody clear.
- `ReceiveFromCarrierAsync(shipmentId, actingUser, ct)` — branch check at `OriginBranchId`,
  transition(s), custody set.
- `ApplyCarrierStatusAsync(shipmentId, targetStatus, occurredAt, carrierEventReference, ct)` —
  carrier-driven; validates against §3.2/§3.3 carrier-trigger transitions only; no user.
- `OverrideInternationalStatusAsync(shipmentId, targetStatus, reason, actingUser, ct)` — staff
  override transitions only.

All go through the existing `TransitionAsync` machinery (xmin concurrency, immutable
`ShipmentEvent`, audit, outbox). `SortAsync` gains the inbound `ReceivedFromCarrier` from-status.

### 5.9 Resilience

`AddCarrierHttpClient<TProvider>(name)` registers a typed `HttpClient` with a standard pipeline:
per-attempt timeout 10 s; up to 3 retries, exponential backoff with jitter, on 5xx/408/429,
`HttpRequestException` and timeouts; circuit breaker (50% failure over a 30 s window, minimum 10
calls, 30 s break); total timeout 30 s. Booking is retried only because the idempotency key is sent
to the carrier. Circuit-open failures surface as `503 CARRIER_UNAVAILABLE`.

### 5.10 Carrier account management

`/api/v1/international/carrier-accounts` — `international.carrier.manage`: list, get, create,
update (display name, active, polling), `POST {id}/credentials` (replace credentials),
`POST {id}/webhook-secret/rotate` (returns the new secret once, in that response only),
`POST {id}/deactivate`. Webhook URL is returned in account responses.

### 5.11 Read model

`GET /international/shipments/{id}` additionally returns the live booking (no secrets, label
download reference if any), booking history, and the carrier event timeline (source, carrier
status, mapped status, processing outcome, occurred/received times). Internal fields
(`RawPayload`, `RawResponse`) are not returned.

## 6. Authorization

| Permission | Purpose | Granted to |
|---|---|---|
| `international.carrier.manage` (new) | Carrier accounts | OrganizationOwner, OrganizationAdmin |
| `international.book` (new) | Book, cancel, link inbound booking | OperationsManager, HubStaff |
| `international.tracking.manage` (new) | Manual carrier events, status overrides | OperationsManager |
| `international.handoff` (I3, first consumer) | Handoff scan | OperationsManager, HubStaff |
| `international.receive` (I3, first consumer) | Receive scan | OperationsManager, HubStaff |

Shipment-scoped routes apply I3's `InternationalShipmentAccess` visibility rule (not visible →
404). Scans additionally require access to the scanning branch (visible but wrong branch → 400).
Webhooks are authenticated by signature only.

## 7. Errors

| Code | HTTP | When |
|---|---|---|
| `INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` | 400 | International validation failures (reused) |
| `CARRIER_ACCOUNT_VALIDATION_FAILED` | 400 | Account create/update validation |
| `SHIPMENT_VALIDATION_FAILED` / `SHIPMENT_INVALID_TRANSITION` | 400 | Reused, from the gateway |
| `CARRIER_BOOKING_FAILED` | 502 | Carrier rejected or failed the booking (sanitized reason) |
| `CARRIER_UNAVAILABLE` | 503 | Circuit breaker open |

Every validation runs before any carrier call or write. Unknown/invisible resources → 404.

## 8. Security and audit

- Tenant isolation on every new table; cross-tenant ID-manipulation test on every new route.
- Secrets encrypted at rest, never returned (except the one-time rotate response) or logged;
  known secret fields masked in stored raw payloads.
- Webhook: organization derived from the account, constant-time signature check, IP rate limit,
  size cap.
- Carrier AWB accepted from staff only for manual carriers and inbound linking.
- Audit: account create/update/credentials/rotate/deactivate (no secret values); book, cancel,
  inbound link; handoff, receive; status override (before/after + reason); manual carrier events.

## 9. Testing

- **Unit:** every new transition (allowed and rejected) both directions; same-branch inbound
  receive producing two events; `SortAsync` accepting `ReceivedFromCarrier` for inbound only;
  manual provider mapping table; processor outcomes (Applied/Ignored/Rejected/Unmatched, retry
  cap); duplicate and out-of-order events; secret masking; override limited to allowed
  transitions.
- **Integration/API (real Postgres):** outbound book → handoff → fake-carrier webhooks →
  `DeliveredAbroad`; inbound link → poll → `CustomsClearance` → receive → sort/manifest →
  delivered; bad signature 401; duplicate webhook stored once; booking failure keeps
  `CustomsPrepared` + 502; idempotent booking retry; circuit breaker opens after repeated fake
  5xx → 503; status override out of `CustomsHold`; cross-tenant 404, missing-permission 403,
  branch-scope on every route; background tenant scope never leaks across organizations.
- **Test infrastructure:** a test-only `FakeHttpCarrierProvider` (typed `HttpClient` against a
  stub server, HMAC-SHA256 webhook signatures) registered only in the test host.
- **Preparatory:** move `PlatformSettingsJwtRotationTests` into a non-parallel xUnit collection
  so the full `Api.Tests` run is stable enough to verify I4 (it rotates the shared JWT signing key
  and breaks concurrently running tests).

## 10. Docs to update

`INTERNATIONAL.md` (I4 section, status table), `SHIPMENT-LIFECYCLE.md`, `DOMAIN-MODEL.md`,
`API-SPEC.md`, `RBAC.md`.
