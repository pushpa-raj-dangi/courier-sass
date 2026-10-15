# API Specification

## 1. Authentication

Two distinct authentication mechanisms — never conflated:

* **Staff/console sessions** (org users, riders via mobile-web): JWT access token +
  refresh token, refresh rotation on use, tenant (`OrganizationId`) and permission
  claims embedded server-side at issuance, never accepted from the client.
* **Merchant API**: API key (`Authorization: Bearer <key>` or `X-Api-Key`), issued
  per-`Merchant` with a denormalized `OrganizationId` (`CLARIFICATIONS.md` #7).
  Keys are stored hashed; only a prefix is ever displayed again after creation.

## 2. Conventions

* Base path: `/api/v1/...`. Breaking changes require a new version path (`/api/v2`);
  additive/backward-compatible changes (new optional fields, new endpoints) do not.
* Pagination: cursor-based for high-volume list endpoints (`shipments`, `events`);
  `?cursor=&limit=` request, `{ data: [...], nextCursor }` response.
* Filtering/sorting: explicit allow-listed query params per endpoint
  (`?status=&fromDate=&toDate=&sort=-createdAt`) — never raw filter/sort expressions
  from the client.
* Standard error envelope:

```json
{
  "error": {
    "code": "SHIPMENT_INVALID_TRANSITION",
    "message": "Cannot transition shipment from DELIVERED to OUT_FOR_DELIVERY",
    "details": { "shipmentId": "...", "from": "DELIVERED", "to": "OUT_FOR_DELIVERY" },
    "traceId": "..."
  }
}
```

* Idempotency: `Idempotency-Key` header required on POST endpoints that create
  resources with financial/operational effect (`POST /shipments`,
  `POST /shipments/{id}/cancel`, `POST /pickups`). Server stores the key + response
  for a bounded window and replays the original response on retry rather than
  double-creating.
* Rate limiting: per API key (Merchant API) and per IP (public tracking), enforced via
  Redis; `429` responses include `Retry-After`.
* Request logging: every Merchant API request is logged with `OrganizationId`,
  `MerchantId`, `ApiKeyId`, path, status, latency — feeds both `report.read`/audit
  needs and abuse detection.

## 3. Merchant API — Endpoint Catalog (v1)

```http
POST   /api/v1/shipments                 # create shipment (price computed server-side)
GET    /api/v1/shipments/{awb}           # fetch by AWB
GET    /api/v1/shipments                 # list/filter own shipments
POST   /api/v1/shipments/{id}/cancel     # cancel (only from cancellable states)

POST   /api/v1/pickups                   # request a pickup
GET    /api/v1/pickups                   # list own pickup requests
POST   /api/v1/pickups/{id}/cancel       # cancel a pickup request

GET    /api/v1/cod/settlements           # list own settlements
GET    /api/v1/cod/settlements/{id}      # settlement detail + line items

GET    /api/v1/webhooks                  # list configured subscriptions
POST   /api/v1/webhooks                  # register a webhook endpoint
DELETE /api/v1/webhooks/{id}
GET    /api/v1/webhooks/{id}/deliveries  # delivery log for a subscription
POST   /api/v1/webhooks/deliveries/{id}/replay

POST   /api/v1/merchant/api-keys         # create an API key (plaintext shown once)
GET    /api/v1/merchant/api-keys         # list own keys (prefix + metadata only)
DELETE /api/v1/merchant/api-keys/{id}    # revoke a key
```

All list/detail endpoints are implicitly scoped to the authenticated Merchant —
`MerchantId` is never a request parameter the client supplies to select *whose* data
to return.

**Credential kinds accepted per route group.** `Shipments`, `Webhooks`, `Pickups`, and
`GET /cod/settlements*` all accept *either* a merchant JWT (`Authorization: Bearer <jwt>`,
scheme `Bearer`) *or* an API key (scheme `ApiKey`) — both schemes are registered on those
route groups and either credential, once resolved to a `(OrganizationId, MerchantId)` pair,
is treated identically downstream. `POST/GET/DELETE /api/v1/merchant/api-keys` is the one
deliberate exception: it stays JWT-only (`CLARIFICATIONS.md` #13's self-escalation
carve-out) so a key can never create, list, or revoke another key — including itself.

## 4. Public Tracking

```http
GET /track/{awb}
```

No authentication. Rate-limited per IP. Looks up `Shipment.Awb` with
`IgnoreQueryFilters()` (there is no tenant context on an anonymous request) but fails
closed — if more than one `Shipment` row matches the AWB, the endpoint returns not-found
rather than arbitrarily picking one (`CLARIFICATIONS.md` #13; `Awb` is unique only per
`(OrganizationId, Awb)`, not globally, so a same-day cross-org collision is possible).

Response shape (`PublicTrackingResponse`):

```json
{
  "awb": "ACME20260820000001",
  "currentStatus": "OutForDelivery",
  "events": [
    { "status": "Received", "timestamp": "2026-08-20T04:15:00Z", "location": "Kathmandu Hub" },
    { "status": "PickedUp", "timestamp": "2026-08-20T06:02:00Z", "location": "Kathmandu Hub" },
    { "status": "InTransit", "timestamp": "2026-08-20T09:40:00Z", "location": null },
    { "status": "OutForDelivery", "timestamp": "2026-08-21T05:10:00Z", "location": "Pokhara Hub" }
  ]
}
```

`status` (both `currentStatus` and each event's `status`) is one of the 8-value
`PublicTrackingStatus` enum, serialized as its member-name string (`[JsonConverter(
typeof(JsonStringEnumConverter))]`, added Frontend Phase F11 — `CLARIFICATIONS.md` #20;
before that it followed the raw-integer wire format most other enums in this codebase
still use, unless individually opted in): `Received, PickedUp, InTransit,
AtDestinationBranch, OutForDelivery, Delivered, DeliveryAttemptUnsuccessful, Cancelled`.
This is a coarser, public-safe mapping of the real 17-value internal `ShipmentStatus`
(16 before I3 added `CustomsPrepared`, which `PublicTrackingStatusMapper.Map` buckets into
`InTransit` alongside `AtOriginBranch`/`Sorted`), not the illustrative CLAUDE.md §24 status
list verbatim (`PublicTrackingStatusMapper.Map`, `CLARIFICATIONS.md` #13 for the three
mapping corrections made during implementation).
`location`, when present, is a city/hub-level branch name only. Consecutive internal
events that map to the same public status (e.g. `AtOriginBranch` then `Sorted`, both
`InTransit`) collapse into a single `events[]` entry.

Explicitly excluded: internal staff identities, internal notes/metadata, financial
fields (COD amount, charges), branch security/location detail beyond a
city/hub-level label, sensitive recipient PII beyond what the requester already
knows (the AWB itself is treated as the shared secret for this endpoint).

**IP rate limiting behind the frontend BFF.** The Next.js merchant/public frontend
(`src/web`) calls this endpoint through its own unauthenticated BFF route
(`app/api/track/[awb]/route.ts`), not directly from the browser (the backend has no
CORS policy for direct browser calls). Since every browser visitor would otherwise
share the BFF's single connection IP, `ForwardedHeadersMiddleware` is wired from a
`ForwardedHeaders:KnownProxies`/`KnownNetworks` configuration section (empty by
default; a real deployment must configure its actual load balancer/ingress network)
and the BFF forwards an inbound `X-Forwarded-For` verbatim — see `CLARIFICATIONS.md`
#20 for the full rationale, including why an unrestricted trust config would be worse
than the problem it fixes (a direct caller could spoof the header to bypass the limit).
`src/web`'s own public surface for this endpoint is `/track` (AWB search) and
`/track/{awb}` (results timeline), added Frontend Phase F11.

## 5. Webhooks (CLAUDE.md §26)

Events: `shipment.created`, `shipment.picked_up`, `shipment.in_transit`,
`shipment.out_for_delivery`, `shipment.delivered`, `shipment.delivery_failed`,
`shipment.ndr_created`, `shipment.rto`, `cod.collected`, `cod.settled`.

* **Delivery**: outbox-driven (see `ARCHITECTURE.md`) — an event becoming durable in
  the outbox is what guarantees a webhook eventually fires, not an inline HTTP call
  during the original request.
* **Signature**: `X-Webhook-Signature: sha256=<HMAC>` computed over the raw payload
  with a per-subscription secret; receivers verify before trusting the payload.
* **Retry**: exponential backoff on non-2xx/timeout, capped attempts, then moved to a
  failed-delivery state visible via `GET /webhooks/{id}/deliveries`.
* **Replay**: `POST /webhooks/deliveries/{id}/replay` re-sends a specific past
  delivery on demand (e.g. after the merchant fixes their endpoint).
* **Delivery log**: every attempt (status code, latency, timestamp, response
  snippet) is retained for merchant visibility and support diagnosis.
* **Auth**: every `/api/v1/webhooks*` management route accepts either a merchant
  JWT or an API key (§3's dual-credential note) — a merchant can register/list/
  delete subscriptions and inspect/replay deliveries from either an interactive
  session or a server-to-server integration using the same credential kinds as
  Shipments and Pickups.

## 6. Internal (staff-facing) API

Same conventions (versioning, error envelope, pagination) but authenticated via
staff JWT and additionally permission/branch-scope checked per `RBAC.md` on every
endpoint — e.g. `POST /api/v1/manifests/{id}/receive` requires `manifest.receive` for
the manifest's `ToBranchId`. Full internal endpoint catalog is enumerated per-module
as each module is implemented (`ROADMAP.md`), not upfront here, to avoid the
catalog drifting from what's actually built.

### 6.1 Shipments (staff and merchant list/detail, plus staff events)

```http
GET    /api/v1/shipments              # list/filter shipments (staff + merchant)
GET    /api/v1/shipments/{awb}        # fetch single shipment by AWB (staff + merchant, branch scope for staff)
GET    /api/v1/shipments/{awb}/events # fetch shipment events (staff only)
```

#### `GET /api/v1/shipments`

Requires `shipment.read` (staff) or `ShipmentRead` (merchant). Query parameters (all optional, AND-combined, applied **after** the existing tenant + branch-scope (staff) / merchant pin (merchant) predicates):

| Param | Type | Semantics |
|---|---|---|
| `status` | repeatable `ShipmentStatus` | `IN` match on `CurrentStatus`. Unknown value → `400 SHIPMENT_VALIDATION_FAILED`. |
| `q` | string | Trimmed. Length < 2 → `400 SHIPMENT_VALIDATION_FAILED`. Matches AWB prefix (case-insensitive, `ILIKE 'q%'`) **or** recipient name contains (case-insensitive) **or** recipient phone contains. `%`/`_` in input are escaped. |
| `merchantId` | Guid | Equality. Ignored-by-construction for merchant callers (their pin already applies; a different id yields an empty page). |
| `serviceTypeId` | Guid | Equality. |
| `branchId` | Guid | Origin **or** destination **or** current branch equals it. If outside the caller's accessible set, the branch-scope predicate already excludes everything → empty page (no 403, no leak). |
| `createdFrom`, `createdTo` | `DateTimeOffset` | Inclusive range on `CreatedAt`, normalized to UTC. `createdFrom > createdTo` → `400 SHIPMENT_VALIDATION_FAILED`. |

Ordering: `CreatedAt desc, Id`. Pagination: cursor-based. Response: `{ data: [ShipmentResponse], nextCursor }`. `ShipmentResponse` gains `DateTimeOffset? LastEventAt` (the max `ShipmentEvent.Timestamp` for the shipment, or null if no events exist).

#### `GET /api/v1/shipments/{awb}`

Requires `shipment.read` (staff) or `ShipmentRead` (merchant). Resolves the shipment by AWB
under tenant isolation. Staff callers outside the shipment's branch scope (origin, destination, or current branch) receive `404`. Merchant callers are pinned to their own merchant and receive `404` for shipments belonging to other merchants.

Response: `ShipmentResponse` including `LastEventAt`.

#### `GET /api/v1/shipments/{awb}/events`

Staff only: requires `shipment.read` (staff without it → `403`); a merchant caller → `403`;
unauthenticated → `401`. Shipment resolved by AWB
under tenant isolation and the branch-scope rule; not found or out of scope → `404`.

Response: array ordered by `Timestamp` ascending, then `Id`:

```json
[{ "id": "…", "eventType": "ManifestReceived", "previousStatus": "InTransit",
   "newStatus": "AtDestinationBranch", "branchId": "…", "userId": "…", "riderId": null,
   "timestamp": "2026-10-05T08:15:00Z", "notes": "…" }]
```

Fields: `id`, `eventType` (serialized as a string), `previousStatus`, `newStatus` (both
serialized as strings), `branchId`, `userId`, `riderId`, `timestamp`, `notes`.

**Not exposed:** `Metadata`, `Latitude`, `Longitude`. Read-only; not audited (consistent
with other reads).

### 6.2 COD (staff)

Staff endpoints for the COD finance console (F15 R3a). Merchant callers use §3 instead.

#### `GET /api/v1/cod/flow`

Staff only (`cod.read`); merchant callers → 403. Query: `branchId` (narrows within scope), `from`,
`to` (inclusive, on COD collection `CreatedAt`, normalised to UTC; `from > to` → 400
`COD_VALIDATION_FAILED`). Returns six cumulative ("reached") stages, in order:
`expected, collected, riderDeposited, branchReconciled, financeVerified, settled`, each
`{ key, amount, count }`. `expected` sums `ExpectedAmount` over all rows; later stages sum
`CollectedAmount` over rows that reached that status or later. Parcel counts never increase left
to right; amounts never increase from `collected` onward (`expected` can be below `collected`
when riders collect more than expected).

#### `GET /api/v1/cod/rider-cash`

Staff only (`cod.read`); merchant callers → 403. Query: `branchId`. Cash collected but not yet
deposited (`CodCollected`), one row per rider + branch:
`{ riderId, branchId, parcelCount, amount, oldestCollectedAt, latestCollectedAt }`, oldest first.

#### Changes to existing COD staff endpoints

- `GET /api/v1/cod/rider-settlements` takes `from` / `to` (on `SettledAt`, UTC; `from > to` → 400
  `COD_VALIDATION_FAILED`). Each row gains `expectedAmount` (Σ collected amount of the settlement's
  linked collections) and `variance` (`declaredAmount − expectedAmount`; negative = short).
- `GET /api/v1/cod/collections` takes a `riderCashSettlementId` filter.
- Date range on the other `/cod` list tabs (post-R3a follow-up): `from` / `to` (inclusive, UTC
  normalised, `from > to` → 400 `COD_VALIDATION_FAILED`) on
  - `GET /api/v1/cod/collections` — on `CreatedAt` (the same field `/cod/flow` uses);
  - `GET /api/v1/cod/branch-settlements` — on `ReconciledAt`;
  - `GET /api/v1/cod/merchant-settlements` — settlements whose period overlaps the range
    (`PeriodTo >= from` and `PeriodFrom <= to`);
  - `GET /api/v1/cod/adjustments` — on `RequestedAt`.
  Merchant settlements and adjustments have no branch, so they take no `branchId`. The merchant
  route `GET /api/v1/cod/settlements` is unchanged.
- Every COD collection response (`GET /api/v1/cod/collections`, `/collections/{id}`) gains `awb`
  (string, `null` only if the shipment lookup misses), loaded with one batched lookup per page.

### 6.3 Manifests (staff)

```http
GET /api/v1/manifests                       # list (array), branch-scoped
GET /api/v1/manifests/{id}                  # single
GET /api/v1/manifests/{id}/missing-shipments
GET /api/v1/manifests/{id}/shipments        # every parcel on the manifest (F15 R3b-1)
```

All require `manifest.read`. **Single reads are branch-scoped** (F15 R3b-1 fix): `/{id}`,
`/missing-shipments` and `/shipments` return `404` unless the manifest's `FromBranchId` or
`ToBranchId` is in the caller's `manifest.read` scope — the same rule as the list.

- `GET /manifests` takes `branchId`, `status` and `direction=incoming|outgoing`. `incoming` keeps
  manifests whose `ToBranchId` is `branchId` (or, with no `branchId`, any branch in scope);
  `outgoing` the same on `FromBranchId`; omitted = either side. An invalid value → `400
  MANIFEST_VALIDATION_FAILED`. Ordered `CreatedAt` descending, not paginated.
- Every manifest response has `receivedCount` — parcels receive-scanned at the destination.
- `/shipments` returns `[{ id, shipmentId, awb, scannedAt, receivedAt }]` ordered by `scannedAt`;
  `/missing-shipments` returns `[{ shipmentId, awb, scannedAt }]` (unreceived rows). Both resolve
  AWBs with one batched lookup.

### 6.4 Delivery attempts (staff)

`GET /api/v1/shipments/{shipmentId}/delivery-attempts` — `ndr.read`, staff only (merchant callers
→ `403`). `404` unless the shipment exists in the tenant and its current, origin or destination
branch is in the caller's `ndr.read` scope. Response, newest first (`attemptedAt`, then
`attemptNumber`, descending), not paginated:

```json
{ "items": [{ "attemptNumber": 2, "attemptedAt": "2026-09-30T04:20:00Z", "succeeded": false,
  "reason": "CustomerUnavailable", "notes": "Gate locked", "customerContacted": true,
  "nextAction": "Reattempt", "riderId": "…" }] }
```

`riderId` is the rider's id (not a user id). GPS is not exposed.

### 6.5 NDR list filters (staff)

`GET /api/v1/ndrs` adds `assigned=me|unassigned` (`me` = the caller owns it; invalid → `400
NDR_VALIDATION_FAILED`) and `reason=<NdrReason>` (invalid → `400`). Both narrow in SQL within the
existing branch scope, before paging.

## 7. International Shipments API (I3, extended I4)

Staff-only (JWT + permission), not part of the Merchant API catalog in §3 — merchant-facing
international booking is I7. Every route checks its `international.*` permission (`403` if
missing) **and** branch scope (`RBAC.md` §3): `POST /` requires access to `originBranchId`; the
shipment-scoped GET/PATCH/action routes return `404` to a branch-restricted caller when the
shipment touches none of their branches as origin, destination or current branch (same rule as
`GET /api/v1/shipments`); `prepare-customs` and `handoff` additionally require access to the
shipment's **current** branch (`400 SHIPMENT_VALIDATION_FAILED`), and `receive` requires access to
the shipment's `originBranchId`. Cross-tenant ids are `404`.

```http
POST   /api/v1/international/shipments                       # create an international shipment shell + InternationalLeg
GET    /api/v1/international/shipments/{id}                  # merged Shipment + InternationalLeg + CustomsItem + booking/tracking detail (I4)
PATCH  /api/v1/international/shipments/{id}/customs           # full-replace the customs declaration
POST   /api/v1/international/shipments/{id}/prepare-customs   # trigger the CustomsPrepared transition

# I4 — carrier booking and lifecycle
POST   /api/v1/international/shipments/{id}/book              # book with a carrier (outbound)
POST   /api/v1/international/shipments/{id}/booking/cancel    # cancel a Booked booking before handoff
POST   /api/v1/international/shipments/{id}/inbound-booking    # link an existing carrier booking (inbound)
GET    /api/v1/international/shipments/{id}/booking/label      # download the live booking's stored label
POST   /api/v1/international/shipments/{id}/carrier-events      # record a manual carrier tracking event
POST   /api/v1/international/shipments/{id}/handoff             # handoff scan (outbound custody ends)
POST   /api/v1/international/shipments/{id}/receive              # receive scan (inbound custody resumes)
POST   /api/v1/international/shipments/{id}/status-override      # staff override out of CustomsHold/InternationalException

# I4 — carrier accounts (tenant-level, not shipment-scoped)
GET    /api/v1/international/carrier-accounts                        # list carrier accounts
GET    /api/v1/international/carrier-accounts/{id}                   # get a carrier account
POST   /api/v1/international/carrier-accounts                        # create (returns the one-time webhook secret if generated)
PUT    /api/v1/international/carrier-accounts/{id}                   # update display name / IsActive / PollingEnabled
POST   /api/v1/international/carrier-accounts/{id}/credentials        # replace stored credentials
POST   /api/v1/international/carrier-accounts/{id}/webhook-secret/rotate  # rotate the webhook secret (returned once)

# I4 — anonymous carrier webhook ingest
POST   /api/v1/carrier-webhooks/{webhookPublicId}                    # signed carrier webhook receiver
```

* **`POST /`** — `international.create`. Body (`CreateInternationalShipmentRequest`): `merchantId`,
  `direction` (`Outbound`|`Inbound`), `originBranchId`, `destinationBranchId`, `serviceTypeId`,
  `weightKg`/`lengthCm`/`widthCm`/`heightCm`, `recipientName`/`recipientPhone` and
  `senderName`/`senderPhone`, the Nepal-side party's `provinceId`/`districtId`/`localLevelId`/
  `wardNumber`, the foreign-side party's `city`/`region`/`postalCode`/`countryCode` (which party is
  foreign is derived from `direction`, not sent explicitly), both parties' `addressLine` (reused as
  `Line1` for whichever side is foreign), `items[]` (`description`, `quantity`, `unitValue`), and
  `provisionalTotalChargeAmount`/`provisionalTotalChargeCurrency`. There is no `codAmount` field —
  COD on an international shipment is unrepresentable, not merely rejected. Creation is one
  transaction (shipment shell, items, creation event, audit, `shipment.created` outbox row and the
  `InternationalLeg`): any failure leaves nothing behind. Any `city`/`region`/`postalCode`/
  `countryCode` sent for the Nepal-side party is ignored (stored null). Returns `201` with
  `InternationalShipmentDetailResponse` (`{ shipment, leg }`). Errors: `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` (`direction` not `Outbound`/`Inbound`, null/empty
  `items[]`, `weightKg` ≤ 0, negative `provisionalTotalChargeAmount`, invalid currency code, foreign
  `city`/`region` > 200 or `postalCode` > 20 chars, unserviced/unknown foreign country code) and `400
  SHIPMENT_VALIDATION_FAILED` (merchant or either branch not in the caller's organization, origin
  branch outside the caller's `international.create` branch scope, Nepal-chain or
  `InternationalAddress` shape failures from `ShipmentAddressValidator`).
* **`GET /{id}`** — `international.read`. Returns the merged detail response, including
  `leg.customsItems[]` (`shipmentItemId`, `hsCode`, `customsValueAmount`, `customsValueCurrency`);
  `404` if the shipment has no `InternationalLeg` (not an I3 international shipment), isn't in the
  caller's tenant, or is outside the caller's branch visibility. Note:
  the embedded `shipment` object is the existing `ShipmentResponse` shape unchanged — it does not
  carry `scope`/`direction` or the foreign-side `city`/`region`/`postalCode`/`countryCode` fields
  (see `docs/INTERNATIONAL.md`'s Known limitations). **I4:** the response additionally carries the
  live booking (if any, no secrets — a download reference instead of the label bytes), the full
  booking history for the leg, and the carrier tracking-event timeline (source, carrier status
  code, mapped `ShipmentStatus`, processing outcome, occurred/received timestamps). `RawPayload`/
  `RawResponse` are never returned.
* **`PATCH /{id}/customs`** — `international.manage_customs`. Full-replace body
  (`UpdateCustomsRequest`): `contentsDescription`, `countryOfOrigin`, `purpose` (`Gift`|
  `CommercialSale`|`Sample`|`Return`|`Documents`|`Other`), `incoterm` (`Ddu`|`Ddp`),
  `exportLicenceReference` (nullable), `items[]` (`shipmentItemId`, `hsCode`,
  `customsValueAmount`, `customsValueCurrency` — exactly one entry per the shipment's existing
  `ShipmentItem`s, no duplicates, no subset; replaces every prior `CustomsItem` row for the
  shipment). Sets `CustomsCompletedAt`, which gates `prepare-customs` below. Response is the leg
  including `customsItems[]`. `404` if no matching shipment/leg or outside branch visibility; `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` for an unknown `purpose`/`incoterm`, blank or > 1000-char
  `contentsDescription`, `countryOfOrigin` not a known two-letter country, `exportLicenceReference`
  > 200 chars, null/empty/duplicate/partial `items[]`, an item not belonging to the shipment, a
  blank or > 20-char `hsCode`, a negative `customsValueAmount`, an invalid currency code, or when
  the shipment has already reached `CustomsPrepared` (or any status outside `Created` …
  `AtDestinationBranch`) — the declaration is frozen from then on. Audited with before/after.
* **`POST /{id}/prepare-customs`** — `international.manage_customs`. Triggers the `CustomsPrepared`
  transition (`SHIPMENT-LIFECYCLE.md`'s `CUSTOMS_PREPARED` subsection). `404` if not found or
  outside branch visibility; `400 INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` if `CustomsCompletedAt`
  is still null; `400 SHIPMENT_VALIDATION_FAILED` if the caller lacks branch access to the
  shipment's current branch; `400
  SHIPMENT_INVALID_TRANSITION` if the shipment isn't currently in the expected `Sorted`
  (same-branch)/`AtDestinationBranch` (different-branch) status.

### 7.1 Carrier booking and lifecycle (I4)

* **`POST /{id}/book`** — `international.book`. Requires an `Idempotency-Key` header (≤200 chars);
  replaying the same key returns the original outcome without calling the carrier again (a `Failed`
  replay re-throws the same `502`; a `Pending` replay younger than 2 minutes returns "still in
  progress"). Body: `{ carrierAccountId, serviceLevel?, carrierAwb? }` (`carrierAwb` required only
  when the carrier's capabilities say it assigns no AWB of its own). Requires `Direction ==
  Outbound`, status `CustomsPrepared`, an active in-tenant carrier account, and no other live
  booking on the leg. `400 INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` for every validation failure
  above (including "still in progress" and "already has a carrier booking"); `502
  CARRIER_BOOKING_FAILED` if the carrier rejects/fails the booking (the shipment stays
  `CustomsPrepared`, a new booking may be attempted); `503 CARRIER_UNAVAILABLE` if the circuit
  breaker is open. Returns the booking (`CarrierBookingResponse`: id, carrier account, direction,
  status, carrier AWB, service level, whether a label exists, booked/cancelled/custody timestamps —
  never the raw carrier response or any secret).
* **`POST /{id}/booking/cancel`** — `international.book`. Only while the booking is `Booked` and
  the shipment hasn't been handed to the carrier yet (still `CustomsPrepared`); calls the carrier's
  `CancelAsync` first, then marks the booking `Cancelled` on success. `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` if there is no booked booking or the shipment has
  already moved past `CustomsPrepared`; `502`/`503` on a carrier-side cancel failure.
* **`POST /{id}/inbound-booking`** — `international.book`. Body: `{ carrierAccountId, carrierAwb,
  serviceLevel? }`. Only for `Direction == Inbound` at status `Created` with no live booking on the
  leg; creates a `Booked` booking directly (no carrier call — the foreign carrier already assigned
  the AWB) and, if the account is polling-enabled, enqueues an immediate poll. `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` if the AWB is already linked on that carrier account or
  a live booking already exists.
* **`GET /{id}/booking/label`** — `international.read`. Streams the live booking's stored label
  (whatever content type the carrier returned, or `application/octet-stream`); `404` if there is no
  visible shipment, no `Booked` booking, or no stored label.
* **`POST /{id}/carrier-events`** — `international.tracking.manage`. Body: `{ statusCode,
  occurredAt, location?, description? }`. Requires a `Booked` booking; `statusCode` must be in that
  provider's known vocabulary; `occurredAt` cannot be more than 5 minutes in the future. Inserted
  with `Source = Manual`, attributed to the caller, and audited
  (`carrier_event.manual_recorded`). `400 INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` for an unknown
  status code, a future-dated `occurredAt`, or an oversized `location`/`description`.
* **`POST /{id}/handoff`** — `international.handoff`, branch-scoped to the shipment's *current*
  branch. Requires a `Booked` outbound booking; transitions `CustomsPrepared → HandedToCarrier` and
  clears custody (see `SHIPMENT-LIFECYCLE.md`'s "Carrier lifecycle" subsection). `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` if there is no booked outbound booking; `400
  SHIPMENT_VALIDATION_FAILED` if the caller lacks access to the shipment's current branch, or if the
  booking was concurrently cancelled (the whole handoff transaction rolls back); `400
  SHIPMENT_INVALID_TRANSITION` if the shipment isn't `CustomsPrepared`.
* **`POST /{id}/receive`** — `international.receive`, requires access to the shipment's
  `originBranchId`. Requires a linked `Booked` inbound booking; transitions from
  `InInternationalTransit`/`CustomsClearance` to `ReceivedFromCarrier` (and, at a same-branch
  gateway, immediately on to `AtDestinationBranch` in the same call). `400
  INTERNATIONAL_SHIPMENT_VALIDATION_FAILED` if no inbound booking is linked yet; `400
  SHIPMENT_INVALID_TRANSITION` if the shipment isn't in one of the two valid from-statuses.
* **`POST /{id}/status-override`** — `international.tracking.manage`. Body: `{ targetStatus,
  reason }`; `reason` is required and non-empty (≤2000 chars). Allowed only for the transitions
  `InternationalTransitionRules` marks staff-overridable (out of `CustomsHold`/
  `InternationalException`); audited with before/after status and reason. `400
  SHIPMENT_VALIDATION_FAILED` for a missing/oversized reason; `400 SHIPMENT_INVALID_TRANSITION` for
  a target not in the staff-override table.

### 7.2 Carrier account management (I4)

`/api/v1/international/carrier-accounts` — `international.carrier.manage` on every route
(tenant-level, not shipment-scoped; cross-tenant ids are `404`).

* **`GET /`** / **`GET /{id}`** — list/get. Every response exposes `hasCredentials`/
  `hasWebhookSecret` booleans and the webhook URL (`/api/v1/carrier-webhooks/{webhookPublicId}`) —
  never the secrets themselves.
* **`POST /`** — create. Body: `{ carrierCode, displayName, credentials?, pollingEnabled,
  generateWebhookSecret }`.
  `pollingEnabled`/`generateWebhookSecret` are only accepted when the provider's capabilities
  support them. Returns the account plus the plaintext webhook secret **once**, only in this
  response (or the rotate response below) — never retrievable again. `400
  CARRIER_ACCOUNT_VALIDATION_FAILED` for an unregistered `carrierCode` or an unsupported
  capability request.
* **`PUT /{id}`** — update `displayName`, `IsActive`, `pollingEnabled`. Covers both deactivation
  and reactivation; there is no separate `deactivate` route. An inactive account's webhook calls
  return `404` and it is skipped by polling.
* **`POST /{id}/credentials`** — replace the encrypted credentials. Never returns them back.
* **`POST /{id}/webhook-secret/rotate`** — generates and stores a new webhook secret, returning it
  once in the response body.

### 7.3 Carrier webhook ingest (I4)

```http
POST /api/v1/carrier-webhooks/{webhookPublicId}
```

Anonymous — authenticated only by the carrier account's own signature, resolved from
`webhookPublicId` (never from the payload). Shares the same per-IP rate limit as public tracking
(`RequireIpRateLimit()`, 30 req/min per IP by default). Body capped at 1 MB (`413` over the cap).
Outcomes: `202 Accepted` (stored, queued for processing); `401 Unauthorized` (bad or missing
signature — nothing stored); `400 CARRIER_WEBHOOK_INVALID_PAYLOAD` (valid signature, unparseable
body — nothing stored); `404 Not Found` (unknown/inactive account, no webhook secret configured, or
the account's carrier doesn't support webhooks).
