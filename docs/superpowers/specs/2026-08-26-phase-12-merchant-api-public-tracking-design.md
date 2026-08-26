# Phase 12 — Merchant API, Public Tracking, API Keys — Design Spec

**Scope note:** ROADMAP.md's Phase 12 entry bundles three things: a real Next.js
merchant portal, public tracking, and the full Merchant API (API keys, rate
limiting, idempotency). This spec covers the **backend-only** slice — public
tracking, API keys, rate limiting, idempotency, and completing the Merchant API
catalog (`API-SPEC.md` §3). The frontend merchant portal is deliberately deferred
to a follow-up phase (12b), since only a bare `create-next-app` scaffold exists in
`src/web` today (no shadcn/ui, no feature folders, nothing wired to the API) and
building a real portal UI is a large, separate effort from the backend work here.

## 1. Context

Through Phase 11, every phase has been backend-only. `src/web` is an untouched
Phase 0 `create-next-app` skeleton. The Merchant API today (per a Phase 12
pre-planning survey of the codebase):

- `Shipments` endpoints (`POST /shipments`, `GET /shipments/{awb}`,
  `GET /shipments`, `POST /shipments/{id}/cancel`) already accept **both** staff
  JWT and merchant JWT via manual per-endpoint branching (no unified auth filter)
  — see `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`.
- `Cod` settlement endpoints are **staff-only** today — no merchant-facing read
  path exists for `MerchantSettlement`/`MerchantSettlementItem`.
- `Webhooks` endpoints exist but are **merchant-JWT-only**
  (`RequireMerchantPermission`), even though `API-SPEC.md` §3 lists them under
  the Merchant API catalog.
- `POST /api/v1/pickups` does not exist anywhere. The codebase's only existing
  "pickup" concept is the rider's pickup scan (Phase 8, staff/rider-driven) — a
  merchant "request a pickup" is a distinct concept never built.
- No `ApiKey` entity/interface exists anywhere in `src/`.
- Only one JWT scheme (`Bearer`) is registered app-wide; staff and merchant
  tokens share it, differentiated only by claims (`GetUserId()` vs
  `GetMerchantId()`). There is no precedent in this codebase for a second
  registered auth scheme.
- Redis is provisioned in `docker-compose.yml` and health-checked
  (`AddHealthChecks().AddRedis(...)`) but never actually used — no
  `IConnectionMultiplexer`, no `StackExchange.Redis` package reference anywhere.

## 2. Goals / Exit Criteria

Per `ROADMAP.md`: *"an external client can create a shipment, poll its status,
and see a settlement purely through the documented Merchant API; public tracking
correctly withholds the excluded fields."*

Concretely:

- An `ApiKey` can be issued for a merchant, used to authenticate Merchant API
  calls, and revoked.
- `POST /shipments`, `GET /shipments/{awb}`, `GET /shipments`,
  `POST /shipments/{id}/cancel` accept API-key auth (in addition to existing
  merchant JWT).
- `GET /cod/settlements`, `GET /cod/settlements/{id}` exist, merchant-scoped,
  accept both credential types.
- `POST /pickups`, `GET /pickups` exist, backed by a real `PickupRequest`
  entity with branch-scoped staff visibility.
- `GET/POST/DELETE /webhooks*` accept API-key auth in addition to existing
  merchant JWT.
- `GET /track/{awb}` is public, rate-limited per IP, and returns only the
  CLAUDE.md §24 safe subset.
- Merchant API requests are rate-limited per API key; public tracking is
  rate-limited per IP; both backed by real Redis.
- `Idempotency-Key` is enforced on `POST /shipments`,
  `POST /shipments/{id}/cancel`, `POST /pickups` for merchant-track callers
  (JWT or API key) — not for staff JWT callers on the same routes, to avoid
  retrofitting every existing staff-facing test from prior phases.

## 3. Non-Goals (explicit deferrals)

- The Next.js merchant portal UI (12b).
- Rider-assignment wiring for `PickupRequest` (future connection to Phase 8's
  `DeliveryRun`) — this phase makes the request real and visible, not
  auto-dispatched.
- API-key scope *selection* at creation time beyond "all of this merchant's
  permission catalog" (a scope-picker UI belongs to the portal, 12b).
- Any change to staff-JWT idempotency behavior on the shared shipment routes.

## 4. Modules & Entities

### 4.1 `ApiKey` (in the existing `Merchants` module)

```
ApiKey
├── Id
├── MerchantId
├── OrganizationId       (denormalized, CLARIFICATIONS.md #7 — kept consistent
│                          with Merchant.OrganizationId by construction: written
│                          once at creation from the resolved Merchant row, never
│                          independently settable)
├── HashedKey             (SHA-256 of the full secret)
├── Prefix                (first 8 chars of the plaintext key, indexed, used for
│                          O(1) lookup before hash comparison)
├── Scopes[]               (MerchantPermissionCatalog strings — reuses the
│                          existing catalog, no new permission model)
├── IsActive
├── CreatedAt
├── RevokedAt
└── LastUsedAt
```

The plaintext key (`{Prefix}.{Secret}`) is generated server-side, shown exactly
once in the `POST /api/v1/merchant/api-keys` response body, and never
recoverable again — only `Prefix` is shown in subsequent list reads. Key
management (`POST/GET/DELETE /api/v1/merchant/api-keys`) is merchant-JWT-only
(Owner role) — an API key cannot manage other API keys, avoiding a
self-escalation surface.

At creation, `Scopes[]` defaults to every permission the creating Owner's own
`MerchantPermissionCatalog` role grants (i.e. all Owner permissions) — no
per-scope picker UI exists yet (that's 12b); the field is real and enforced,
just not selectively assignable through an API this phase.

### 4.2 `PickupRequest` (in the existing `Shipments` module)

```
PickupRequest
├── Id
├── OrganizationId
├── MerchantId
├── BranchId              (resolved from the shipments' OriginBranchId — must
│                          be consistent across all ShipmentIds in one request)
├── ShipmentIds[]
├── RequestedForDate
├── Notes
├── Status                 (Requested | Scheduled | Completed | Cancelled)
├── CreatedAt
└── UpdatedAt
```

`POST /api/v1/pickups` — merchant-track only (JWT or API key), validates every
`ShipmentId` belongs to the calling merchant and shares one `OriginBranchId`.
`GET /api/v1/pickups` — dual-purpose: merchant-track callers see their own
requests; staff callers see requests for their accessible branches
(`GetAccessibleBranchIdsAsync`, same pattern as every other branch-scoped list
in this codebase). No status-transition endpoint this phase beyond merchant
`Cancelled` — staff acting on a request (`Scheduled`/`Completed`) is out of
scope until the Phase 8 `DeliveryRun` wiring lands.

### 4.3 Redis infrastructure (`SharedKernel`)

First real (non-health-check) use of Redis in this codebase:

```csharp
public interface IRateLimiter
{
    Task<RateLimitResult> TryAcquireAsync(string partitionKey, int limit, TimeSpan window, CancellationToken ct = default);
}

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> GetAsync(string merchantScopedKey, CancellationToken ct = default);
    Task StoreAsync(string merchantScopedKey, IdempotencyRecord record, TimeSpan ttl, CancellationToken ct = default);
}
```

Both backed directly by `StackExchange.Redis`'s `IConnectionMultiplexer`,
registered once in `Program.cs` from the same Redis connection string
`docker-compose.yml`/health checks already use.

## 5. Auth: `ApiKey` Scheme + Unified Permission Check

`ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>`
registered as scheme `"ApiKey"` — the app's second-ever registered scheme (today
staff and merchant JWT share one `"Bearer"` scheme).

Flow: reads `Authorization: Bearer <key>` or `X-Api-Key`, splits `{Prefix}.{Secret}`,
looks up `ApiKey` by `Prefix` (indexed), verifies the SHA-256 hash matches,
`IsActive == true`, `RevokedAt is null`. On success, builds a `ClaimsPrincipal`
carrying:

- the **same claim types** `GetMerchantId()`/`GetOrganizationId()` already read
  from a merchant JWT (so every existing `ResolveCaller`-style helper works
  unchanged for either credential),
- a new `api_key_scopes` claim (space-joined `Scopes[]`).

Updates `ApiKey.LastUsedAt` inline (single-row update, acceptable at this
scale). Failure modes map to `401` with `API_KEY_INVALID` / `API_KEY_REVOKED`.

Merchant-reachable endpoint groups move from bare `.RequireAuthorization()` to:

```csharp
.RequireAuthorization(policy => policy
    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey")
    .RequireAssertion(ctx => ctx.User.Identity?.IsAuthenticated == true))
```

`Merchants.Infrastructure.MerchantAuthorizationChecker.HasPermissionAsync` gets
one branch added at the top: if the principal carries an `api_key_scopes`
claim, check the requested permission against that claim's value directly (no
`MerchantUser`/role lookup — an API key isn't a user). Otherwise, fall through
to the existing `MerchantUser` role-lookup path unchanged. One unified
permission-check entry point serves both credential types; no endpoint's
authorization logic needs to know which one it's looking at.

## 6. Rate Limiting

Hand-rolled Redis fixed-window counter — not ASP.NET Core's built-in
rate-limiting middleware (designed around in-process partitions, doesn't fit a
multi-instance Redis store) and not a third-party Redis-rate-limit package
(CLAUDE.md §34 — don't introduce a library without justification; a fixed-window
`INCR`+`EXPIRE NX` is ~20 lines):

```
key = "ratelimit:{partitionKey}:{windowBucket}"
INCR key
if new value == 1: EXPIRE key windowSeconds NX
if new value > limit: reject (429, Retry-After = TTL of key)
```

Applied via a thin middleware wrapping only the Merchant API group (partition
`apikey:{ApiKeyId}`, default 60 req/min, configurable via `appsettings`) and
the public tracking endpoint (partition `ip:{ClientIp}`, default 30 req/min) —
not applied app-wide. `429` responses use error code `RATE_LIMIT_EXCEEDED`.

## 7. Idempotency

`idem:{MerchantId}:{IdempotencyKey}` → `{RequestBodyHash, StatusCode,
ResponseBody}` in Redis, 24h TTL. A filter wraps `POST /shipments`,
`POST /shipments/{id}/cancel`, `POST /pickups`, active **only when the caller
authenticated as a merchant** (JWT or API key) — staff JWT callers on the same
three routes are completely untouched, avoiding any change to existing
staff-facing tests from prior phases.

- Header missing on one of the three routes for a merchant caller →
  `400 IDEMPOTENCY_KEY_REQUIRED`.
- Key seen before, same request-body hash → replay the stored response
  verbatim, no re-execution.
- Key seen before, different request-body hash → `409 IDEMPOTENCY_KEY_REUSED`.
- Key not seen → execute normally, cache the result before returning.

## 8. Merchant API Catalog Completion

- **COD settlements:** new merchant-reachable `GET /api/v1/cod/settlements`,
  `GET /api/v1/cod/settlements/{id}` in the existing `Cod` module, dual-scheme,
  reading the same `MerchantSettlement`/`MerchantSettlementItem` data the
  existing staff-only `/cod/merchant-settlements` route already serves, scoped
  to the caller's own `MerchantId` (never a supplied parameter). The existing
  staff route is untouched — this is an additive merchant-facing read path.
- **Pickups:** `POST/GET /api/v1/pickups` per §4.2.
- **Webhooks:** existing `/api/v1/webhooks*` routes gain the `ApiKey` scheme
  alongside their existing `RequireMerchantPermission` (merchant-JWT) path —
  matches `API-SPEC.md` §3 listing them under the Merchant API catalog.

## 9. Public Tracking

`GET /track/{awb}` — no auth, rate-limited per IP (§6). A new read-only mapping
service in `Shipments` collapses the full internal `ShipmentEvent` history down
to the CLAUDE.md §24 safe subset:

```
Internal ShipmentEvent → Public TrackingEvent
─────────────────────────────────────────────
Created / AtOriginBranch                → Received
PickedUp / RiderPickupScan              → PickedUp
Sorted / Dispatched / InTransit          → InTransit
AtDestinationBranch                     → AtDestinationBranch
OutForDelivery                          → OutForDelivery
Delivered                               → Delivered
DeliveryFailed / NdrCreated /
RtoInitiated / RtoInTransit /
RtoAtOrigin / RtoDelivered              → DeliveryAttemptUnsuccessful
```

Each public event carries only a status label and a timestamp, plus a
city/hub-level label (never a branch ID, address, or coordinates). Explicitly
excluded fields (never present anywhere in the response): internal staff/rider
identity, notes/metadata, `CODAmount`/charges/any financial field, branch
security/location detail beyond the city/hub label, recipient PII beyond what
the requester already possesses. The AWB itself is the shared secret — no
additional lookup key is required or accepted.

## 10. Request Logging

Per `API-SPEC.md` §2: every `ApiKey`-authenticated request gets one structured
Serilog entry (`OrganizationId`, `MerchantId`, `ApiKeyId`, path, status,
latency) written by a thin middleware after the response completes, reusing
the correlation-ID pattern `ARCHITECTURE.md` already establishes — no new sink.

## 11. Error Codes

New codes added to the existing error-envelope catalog style (matching
`WEBHOOK_VALIDATION_FAILED` etc.): `API_KEY_INVALID`, `API_KEY_REVOKED`,
`RATE_LIMIT_EXCEEDED`, `IDEMPOTENCY_KEY_REQUIRED`, `IDEMPOTENCY_KEY_REUSED`.

## 12. Testing

- Unit: `ApiKeyAuthenticationHandler` (valid / revoked / inactive / malformed
  key), the Redis-backed rate limiter and idempotency store — against a real
  `IConnectionMultiplexer` on the existing dev Redis container, not mocked
  (TTL/atomicity behavior is exactly the class of thing an in-memory fake gets
  wrong, matching this project's established "test against the real
  infrastructure it actually depends on" pattern from Phase 10's Postgres
  concurrency tests and Phase 11's end-to-end RabbitMQ/SMTP tests).
  `PickupRequest` branch-scoping. The public-tracking event-mapping function
  (every internal status → exactly one safe public value, no internal status
  ever leaks through unmapped).
- Integration/API: one real end-to-end test proving the exit criteria
  literally — an external client creates a shipment via API key, polls it by
  AWB, and reads a settlement, purely through Merchant API routes, no JWT
  involved anywhere in that test. A public-tracking test asserting excluded
  fields never appear anywhere in the raw response body (not just "aren't in
  the DTO" — a serialization-level string-search assertion on the response
  JSON). A forced-rate-limit test proving `429` + `Retry-After` fires past the
  configured threshold. An idempotency test proving same-key-same-body
  replays verbatim, same-key-different-body returns `409`, and — critically —
  a staff-JWT call to the same `POST /shipments` route without an
  `Idempotency-Key` header still succeeds unmodified (proving the merchant-only
  scoping decision from §7 didn't regress existing staff flows).

## 13. Tenant/Branch Isolation Checklist (per this project's established pattern)

- `ApiKey`: has its own `OrganizationId` — direct filter, plus the
  `MerchantId`-ownership check on every management endpoint.
- `PickupRequest`: has its own `OrganizationId` and `BranchId` — direct filter
  on both; merchant-track reads additionally scope to `MerchantId == self`.
- COD settlement merchant reads: scope to `MerchantId == self`, never a
  supplied parameter — mirrors the exact pattern already proven safe in
  `ShipmentsEndpoints`'s existing merchant-scoped reads.
- Public tracking: no tenant/org concept exposed at all — AWB is the only
  input, response contains no ID a caller could pivot from to enumerate other
  orgs' data.
