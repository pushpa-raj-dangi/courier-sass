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
  "currentStatus": 4,
  "events": [
    { "status": 0, "timestamp": "2026-08-20T04:15:00Z", "location": "Kathmandu Hub" },
    { "status": 1, "timestamp": "2026-08-20T06:02:00Z", "location": "Kathmandu Hub" },
    { "status": 2, "timestamp": "2026-08-20T09:40:00Z", "location": null },
    { "status": 4, "timestamp": "2026-08-21T05:10:00Z", "location": "Pokhara Hub" }
  ]
}
```

`status` (both `currentStatus` and each event's `status`) is one of the 8-value
`PublicTrackingStatus` enum, serialized as its integer ordinal (no
`JsonStringEnumConverter` is configured project-wide, so this follows the same
raw-integer wire format every other enum in this codebase already uses):
`0=Received, 1=PickedUp, 2=InTransit, 3=AtDestinationBranch, 4=OutForDelivery,
5=Delivered, 6=DeliveryAttemptUnsuccessful, 7=Cancelled`. This is a coarser,
public-safe mapping of the real 16-value internal `ShipmentStatus`, not the
illustrative CLAUDE.md §24 status list verbatim (`PublicTrackingStatusMapper.Map`,
`CLARIFICATIONS.md` #13 for the three mapping corrections made during implementation).
`location`, when present, is a city/hub-level branch name only. Consecutive internal
events that map to the same public status (e.g. `AtOriginBranch` then `Sorted`, both
`InTransit`) collapse into a single `events[]` entry.

Explicitly excluded: internal staff identities, internal notes/metadata, financial
fields (COD amount, charges), branch security/location detail beyond a
city/hub-level label, sensitive recipient PII beyond what the requester already
knows (the AWB itself is treated as the shared secret for this endpoint).

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
