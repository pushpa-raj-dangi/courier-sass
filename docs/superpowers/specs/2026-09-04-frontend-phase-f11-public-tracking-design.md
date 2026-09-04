# Frontend Phase F11 — Public Tracking — Design Spec

## 1. Context

Backend Phase 12 already shipped `GET /track/{awb}` — genuinely anonymous, IP
rate-limited (`src/Modules/Shipments/Presentation/PublicTrackingEndpoints.cs`),
mapping the full internal `ShipmentStatus`/`ShipmentEvent` history down to the
CLAUDE.md §24 safe subset (`PublicTrackingStatus`, `PublicTrackingEvent`,
`PublicTrackingResponse` in `Application/PublicTrackingDtos.cs`). No frontend
has ever consumed it — `src/web` has no public/unauthenticated route at all
today; `app/page.tsx` unconditionally redirects to `/dashboard` or `/login`,
and every other route lives behind the staff or merchant cookie check in
`middleware.ts`.

Per `docs/FRONTEND-ROADMAP.md`, F11's stated scope is exactly this: build
`/track/{awb}`, unauthenticated, withholding the fields CLAUDE.md §24 lists as
excluded (internal staff/rider identity, notes/metadata, financial fields,
branch security/location detail beyond a city/hub label, recipient PII beyond
what the requester already possesses).

While inspecting the endpoint for this phase, two real backend gaps surfaced —
closed here first, matching every prior frontend phase's established pattern
(F2 through F10 each found and fixed real backend defects before or during
their own UI work).

## 2. Goals / Exit Criteria

- A visitor with only an AWB can open `/track`, enter it, and land on
  `/track/{awb}` showing the current status and a chronological event
  timeline — no login, no cookies, no app-shell chrome.
- The page never renders any field excluded by CLAUDE.md §24.
- `PublicTrackingStatus` serializes as a named string, not a raw integer.
- The per-IP rate limit on `/track/{awb}` is keyed on the real visitor's IP in
  any deployment sitting behind a real load balancer/CDN, not collapsed onto
  the Next.js BFF's single IP — while remaining unspoofable by a caller
  hitting the backend directly.
- 404 (unknown AWB), 429 (rate-limited), and network/5xx are all handled with
  a real UI state, not a crash or a blank page.

## 3. Non-Goals (explicit deferrals)

- No nav entry point wired into `/login` or `/merchant-login` — the
  standalone `/track` surface is the full scope this phase.
- No SMS/email/webhook template changes. Grepped `src/Modules/Notifications`
  for any existing reference to a tracking URL — none exists, so there is no
  existing expectation to satisfy; wiring a real link into notification
  templates is a future notifications-phase concern.
- No polling infrastructure beyond a plain TanStack Query `refetchInterval`
  (no WebSocket/SSE push).
- No change to the Merchant API's or staff app's own (authenticated) shipment
  lookup — this phase only adds the anonymous surface.

## 4. Backend Changes (done first)

### 4.1 `PublicTrackingStatus` — missing `JsonStringEnumConverter`

Confirmed by inspection: this is a plain C# enum with no
`[JsonConverter(typeof(JsonStringEnumConverter))]` and no global string-enum
serializer config covering it, so `PublicTrackingResponse.CurrentStatus` and
every `PublicTrackingEvent.Status` currently serialize as raw integers. This
is the same recurring bug class hit and fixed in F2 (`BranchType`), F3
(`ShipmentStatus`), F4 (`ManifestPurpose`/`ManifestStatus`), F5/F6 (three
`Ndr` enums), F7 (three `Cod` enums) — `PublicTrackingStatus` was simply never
touched by any of those phases. Fix: add the converter attribute directly on
the enum, matching the exact fix shape used every prior time.

### 4.2 IP rate limiter has no reverse-proxy awareness

`IpRateLimitFilter` (`src/SharedKernel/RateLimitFilters.cs`) partitions
strictly on `httpContext.Connection.RemoteIpAddress`. Today nothing calls
`/track/{awb}` through a proxy, so this is latent. Once F11 ships, **every**
browser visitor's request arrives at the backend from the Next.js BFF's own
connection (the backend has no CORS policy configured anywhere, so a direct
browser-to-backend call is not an option — the BFF is structurally required),
collapsing all public-tracking traffic site-wide into one shared 30 req/min
bucket. This is precisely the risk Phase 12's final review flagged and
deferred ("a self-inflicted DoS on `/track/{awb}`" — `docs/CLARIFICATIONS.md`
§13.1).

Fix, scoped narrowly to close this without opening a worse hole:

- Backend: register ASP.NET Core's `ForwardedHeadersMiddleware`
  (`ForwardedHeaders.XForwardedFor`) early in the pipeline, with
  `KnownProxies`/`KnownNetworks` populated from configuration
  (`ForwardedHeaders:KnownProxies` / `ForwardedHeaders:KnownNetworks`), not
  left at an unrestricted default. **This restriction is load-bearing, not
  optional**: if the middleware trusted every caller's `X-Forwarded-For`
  unconditionally, an external caller hitting `/track/{awb}` directly (no
  BFF in the path) could set an arbitrary `X-Forwarded-For` value on every
  request and rotate it to bypass the per-IP limit entirely — strictly worse
  than the shared-bucket problem being fixed. Local docker-compose gets a
  documented default (the compose bridge network's IP range, e.g. matching
  the existing `courier-postgres-1`-style local stack); a real deployment
  must set this to its actual load balancer/ingress network — this decision
  cannot be made generically and is left as required configuration, not
  hardcoded.
- Frontend BFF (`app/api/track/[awb]/route.ts`, new): if the inbound request
  already carries an `x-forwarded-for` header — true in any real deployment,
  since AWS ALB, Azure Application Gateway, and a standard nginx/Vercel edge
  all set it automatically — forward that value verbatim as `X-Forwarded-For`
  on the backend call. If absent (bare local dev, browser talks to the Next
  server directly with no upstream proxy at all), forward nothing; the
  backend falls back to its current behavior, which is not a regression from
  today's (already-latent) state.

Net effect: correct per-visitor limiting in any real deployment, no change to
local dev behavior, and the trust boundary stays closed against spoofing from
a caller that bypasses the BFF entirely.

## 5. Frontend

### 5.1 Routes

Both live outside the `(staff)` and `(merchant)` route groups and are
deliberately absent from `middleware.ts`'s `matcher` — no cookie check, no
app-shell chrome, a self-contained public layout.

- `app/track/page.tsx` — a single AWB input + submit; navigates to
  `/track/[awb]` on submit (client-side `router.push`, not a form GET, so the
  AWB is normalized/trimmed first).
- `app/track/[awb]/page.tsx` — results view: a status headline and a
  vertical timeline of `PublicTrackingEvent`s (status label, timestamp,
  optional city/hub label), built entirely from existing shadcn/ui
  primitives (`Card`, `Badge`, `Separator`, `Skeleton`) per the project's
  standing shadcn-only convention — no new raw HTML primitives.

### 5.2 BFF route

`app/api/track/[awb]/route.ts` (new) — `GET`, no auth. Unlike every existing
BFF route in this codebase, it does **not** go through `apiFetch`
(`src/web/src/lib/auth/apiFetch.ts`), which requires a valid session and
throws `NOT_AUTHENTICATED` otherwise — there is no session here by design. It
calls the backend directly: `fetch(`${getUpstreamApiUrl()}/track/${awb}`,
{ headers: forwardedIpHeader, cache: "no-store" })`, and passes the backend's
status/body straight through unmodified (200 with the tracking payload, 404,
or 429 with its `Retry-After` header forwarded too).

### 5.3 Data fetching

Client-side TanStack Query via the existing `clientFetch` helper
(`src/web/src/lib/api/clientFetch.ts`) — its `SessionExpiredError` branch on
401 is simply never exercised here, since this endpoint never returns 401.
`refetchInterval` stays active while `CurrentStatus` is not yet `Delivered`,
`Cancelled`, or the terminal-of-the-unsuccessful-branch label
(`DeliveryAttemptUnsuccessful` is not itself terminal — RTO/reattempt can
still follow — so only `Delivered`/`Cancelled` stop polling).

### 5.4 States

- **Loading:** `Skeleton`-based placeholder matching the eventual timeline
  shape.
- **404:** a deliberately generic "No shipment found for this AWB" message —
  never confirms or denies *why* (wrong org, cancelled and purged, typo, etc.
  are all indistinguishable to the requester, matching the "AWB is the only
  input, response contains no ID a caller could pivot from" principle Phase
  12's spec already established).
- **429:** a friendly "Too many lookups — try again in Ns" using the
  forwarded `Retry-After` header value.
- **Network/5xx:** generic retry-affordance fallback.

### 5.5 Visual bar

This is the first genuinely public/customer-facing surface in the product —
every other page lives behind a staff or merchant login. It gets a real
design pass (via the `frontend-design`/`impeccable` skill during
implementation) rather than reusing the internal back-office's admin-table
visual density; still built from the same shadcn/ui component set, no new
dependencies.

## 6. Testing

- **Backend:** unit test for the `PublicTrackingStatus` serialization
  round-trip (string, not integer); integration test for
  `ForwardedHeadersMiddleware` behavior — request from a configured trusted
  proxy honors `X-Forwarded-For`, request from an untrusted/direct source
  does not (falls back to the immediate connection IP, not spoofable via the
  header).
- **Frontend:** BFF route test covering header-forwarding logic and 200/404/
  429 pass-through; page-level tests for the loading/success/404/429/error
  states; one manual E2E pass against the real API — book a shipment via the
  existing test-seeding pattern, drive it through a couple of real status
  transitions using the already-built staff/rider flows, confirm the public
  page reflects each transition and never renders any CLAUDE.md §24-excluded
  field (branch id/address/coordinates, staff/rider identity, notes,
  COD/charges).

## 7. Security Notes

- Public tracking exposes no tenant/org concept at all — the AWB is the only
  input and the response contains no ID a caller could pivot from to
  enumerate another org's data (carried over unchanged from Phase 12's own
  security analysis, still true — F11 adds no new backend read path, only a
  UI over the existing one).
- The `ForwardedHeaders` fix is explicitly scoped to a configured trusted-proxy
  allowlist (§4.2) specifically because an unrestricted version would let a
  direct external caller spoof its way past the per-IP rate limit — this is
  the one place in F11 where getting the scope wrong makes security *worse*,
  not just incomplete.
