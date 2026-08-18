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

GET    /api/v1/cod/settlements           # list own settlements
GET    /api/v1/cod/settlements/{id}      # settlement detail + line items

GET    /api/v1/webhooks                  # list configured subscriptions
POST   /api/v1/webhooks                  # register a webhook endpoint
DELETE /api/v1/webhooks/{id}
GET    /api/v1/webhooks/{id}/deliveries  # delivery log for a subscription
POST   /api/v1/webhooks/deliveries/{id}/replay
```

All list/detail endpoints are implicitly scoped to the authenticated Merchant —
`MerchantId` is never a request parameter the client supplies to select *whose* data
to return.

## 4. Public Tracking

```http
GET /track/{awb}
```

No authentication. Rate-limited per IP. Response includes only the customer-safe
subset of `ShipmentEvent`s (CLAUDE.md §24):

* Shipment received / picked up / in transit / at destination branch /
  out for delivery / delivered (+ timestamps)

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

## 6. Internal (staff-facing) API

Same conventions (versioning, error envelope, pagination) but authenticated via
staff JWT and additionally permission/branch-scope checked per `RBAC.md` on every
endpoint — e.g. `POST /api/v1/manifests/{id}/receive` requires `manifest.receive` for
the manifest's `ToBranchId`. Full internal endpoint catalog is enumerated per-module
as each module is implemented (`ROADMAP.md`), not upfront here, to avoid the
catalog drifting from what's actually built.
