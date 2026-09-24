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

## 7. International Shipments API (I3)

Staff-only (JWT + permission), not part of the Merchant API catalog in §3 — merchant-facing
international booking is I7. All four routes check the `international.*` permission (`403` if
missing) **and** branch scope (`RBAC.md` §3): `POST` requires access to `originBranchId`; `GET`/
`PATCH`/`prepare-customs` return `404` to a branch-restricted caller when the shipment touches none
of their branches as origin, destination or current branch (same rule as `GET /api/v1/shipments`),
and `prepare-customs` additionally requires access to the shipment's current branch (`400
SHIPMENT_VALIDATION_FAILED`). Cross-tenant ids are `404`.

```http
POST   /api/v1/international/shipments                       # create an international shipment shell + InternationalLeg
GET    /api/v1/international/shipments/{id}                  # merged Shipment + InternationalLeg + CustomsItem detail
PATCH  /api/v1/international/shipments/{id}/customs           # full-replace the customs declaration
POST   /api/v1/international/shipments/{id}/prepare-customs   # trigger the CustomsPrepared transition
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
  (see `docs/INTERNATIONAL.md`'s Known limitations).
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
