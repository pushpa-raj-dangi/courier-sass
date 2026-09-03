# Frontend Phase F10 — Merchant Portal — Design

**Date:** 2026-09-03
**Status:** Approved, ready for implementation planning.
**Roadmap entry:** `docs/FRONTEND-ROADMAP.md` "Later" section — "Phase F10 — Merchant
Portal (booking, tracking, COD settlements, invoices, API key management — the
original backend Phase 12b scope)."

## Context

F0–F9 are complete: staff back-office app shell, auth, org/branch dashboards,
shipments, manifests, riders/delivery/POD, NDR/RTO, COD operations,
reports/analytics, and RBAC/admin. All of it is staff-facing — logged in via
`courier_access_token`/`courier_refresh_token` cookies against
`/api/v1/auth/*`.

The backend already has a fully separate merchant-facing track from Phase 12
(`docs/superpowers/specs/2026-08-26-phase-12-merchant-api-public-tracking-design.md`):
merchant JWT login/refresh (`Courier.Merchants` module), an `ApiKey` credential
type, dual-auth (JWT-or-API-key) routes on `Shipments`, `Cod`, `Webhooks`, and
merchant-only routes for profile, KYC, and staff management. None of it has a
frontend. This phase builds that frontend — a separate merchant portal app
under `src/web`, not an extension of the staff app — covering the scope this
roadmap entry names: booking, tracking, COD settlements, invoices, and API key
management. Investigating `src/Modules/Merchants/`, `src/Modules/Cod/`,
`src/Modules/Shipments/`, `src/Modules/Branches/`, and `src/Modules/Pricing/`
before design turned up four real backend gaps. Three map directly to the
resolved design questions below, confirmed with the user during
brainstorming; the fourth (a merchant-reachable branch list) was found
during the backend investigation itself and is folded in as Backend changes
§4.

## Resolved design questions (confirmed with user during brainstorming)

1. **Invoices** — no `Invoice` entity exists anywhere in the backend
   (`CLAUDE.md` §15/§25 mention invoices, but Phase 10/12 only built
   `MerchantSettlement`/`MerchantSettlementItem`, which already carry
   COD/courier-fee/net-payable per shipment for a period — exactly
   `CLAUDE.md` §22's worked example). Decision: treat each
   `MerchantSettlement` as the invoice. No new entity, no migration, no PDF
   service — a printable detail Sheet plus a client-side CSV export of
   already-fetched JSON.
2. **Branch selection at booking** — no geography-to-branch auto-resolution
   service exists (confirmed: `Branch` deliberately has no Area-level FK for
   this, per Phase 5's own design notes). Decision: the merchant picks
   Origin/Destination branch from a dropdown, exactly like the staff
   `ShipmentCreateSheet` already does. No backend change.
3. **Session model** — decision: a fully separate portal mirroring the staff
   app's own cookie/session/middleware pattern exactly (own cookies, own BFF
   routes, own route group, own login page), not a generalization of the
   staff session code to carry two credential types.

## Backend changes

### 1. Enum serialization fix

`KycDocumentType` and `MerchantUserRole`
(`src/Modules/Merchants/Domain/KycDocumentType.cs`,
`.../MerchantUserRole.cs`) have no `[JsonConverter(typeof(JsonStringEnumConverter))]`.
Same latent bug class hit and fixed in every prior phase (`BranchType` F2,
`ShipmentStatus` F3, `ManifestPurpose`/`ManifestStatus` F4, 5 Riders enums F5,
3 Ndr enums F6, 3 Cod enums F7). Without it, `POST
/api/v1/merchant/kyc-documents?documentType=...` cannot deserialize a string
query value into the enum, and any response embedding `MerchantUserRole`
(`MerchantStaffResponse.Role`) serializes as a raw integer, breaking
`StatusBadge`/role-label rendering the same way every prior instance did.
Fix: add the attribute to both enums, matching `KycStatus`'s existing
attribute in the same folder exactly.

### 2. `GET /api/v1/merchant/auth/me`

New endpoint on `MerchantAuthEndpoints`, JWT-only (same reasoning as the
existing `ApiKeyEndpoints` self-escalation carve-out — an API key has no
`MerchantUser` row to describe). Mirrors staff's `GET /api/v1/auth/me`
(`Identity.Application.AuthDtos.MeResponse`) shape and purpose: the one call
the frontend session hook needs to know who is logged in and what they can
see.

```csharp
public record MerchantMeResponse(
    Guid MerchantUserId, Guid MerchantId, Guid OrganizationId,
    string MerchantDisplayName, string Email, string FullName,
    MerchantUserRole Role, IReadOnlyList<string> Permissions, KycStatus KycStatus);
```

New `MerchantAuthService.GetMeAsync(ClaimsPrincipal user, CancellationToken)`:
resolve `MerchantUserId` via `user.GetUserId()` (merchant JWT `sub` claim,
same accessor the JWT path already uses elsewhere), load the `MerchantUser`
and its `Merchant` (`IgnoreQueryFilters()`, same reasoning as `LoginAsync`'s
existing lookups — there is no tenant-scoped `DbContext` filter active for a
merchant-JWT-authenticated request), return 401 if either row is missing or
inactive. `Permissions` is `MerchantPermissionCatalog.RolePermissions[role].ToList()`
— already computed today inside `MerchantAuthorizationChecker`, just not
exposed. Route: `group.MapGet("/me", ...)` on the existing
`/api/v1/merchant/auth` group, `.RequireAuthorization(AuthSchemes.Bearer)`
(no merchant-permission filter needed — this route only requires being a
valid, active merchant user, the same bar `RequireMerchantPermission` already
enforces internally before any scope check).

### 3. Merchant-callable price quote

`POST /api/v1/pricing/quote` (`PricingEndpoints.cs`) is currently gated
`if (httpContext.User.GetUserId() is null) return Unauthorized();` followed by
`.RequirePermission("pricing.read")` — both staff-only. The merchant booking
form needs the same live quote-preview UX the staff `ShipmentCreateSheet`
already has (`useShipmentQuote`, per Phase F3). Fix: apply the same
manual dual-auth branch `ShipmentsEndpoints.cs` already uses for its own
group (staff JWT via `IPermissionChecker`/`"pricing.read"`, or merchant
JWT/API-key via `IMerchantAuthorizationChecker`/a new
`MerchantPermissionCatalog.PricingRead = "merchant.pricing.read"` constant
added to both `OwnerPermissions` and `StaffPermissions`, since both merchant
roles need to book). Route itself changes from a plain `MapPost` to the
`RequireAuthorization(AddAuthenticationSchemes(Bearer, ApiKey))` pattern
`ShipmentsEndpoints`'s group already establishes.

### 4. Merchant-reachable branch list (booking form's Origin/Destination pickers)

Verified directly (not assumed): `GET /api/v1/branches` (`BranchesEndpoints.cs`)
has no explicit `RequireAuthorization` override, so it falls to the default
policy plus `.RequirePermission("branch.read")`. A merchant JWT *does*
authenticate against the "Bearer" scheme here — `MerchantJwtOptions` is bound
from the same `"Jwt"` config section `Program.cs` uses for the single shared
`AddJwtBearer` registration, so staff and merchant tokens validate under the
one scheme by shared signing key, differing only in claims. But
`RequirePermissionFilter` then calls `IPermissionChecker.HasPermissionAsync`
using `GetUserId()` (the generic `"sub"` claim, which a merchant token also
sets — to the *merchant user's* id) against staff `UserRole`/`RolePermission`
tables, where no matching row exists — the request fails closed with `403`,
not a leak, but also not usable by this phase's booking form. Separately,
`BranchService.ListBranchesAsync` itself calls
`_branchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "branch.read", ...)`,
which resolves staff `UserBranch` grants — meaningless for a merchant caller
even if auth passed.

Fix: new `BranchService.ListBranchesForMerchantAsync(ClaimsPrincipal user, CancellationToken)` —
org-scoped only (via the ambient tenant query filter already applied to
`_dbContext.Set<Branch>()`), returns every active branch with no
accessible-branch-id narrowing (merchants have no branch concept to narrow
by; they need to see every branch as a candidate origin/destination). New
route `GET /api/v1/merchant/branches` (new group, same file or a small new
`MerchantBranchesEndpoints.cs` in `Branches.Presentation`), dual-auth
(`AddAuthenticationSchemes(Bearer, ApiKey)`) gated by a new
`MerchantPermissionCatalog.BranchesRead = "merchant.branches.read"` (added to
both `OwnerPermissions` and `StaffPermissions` — both merchant roles book
shipments). This requires `Branches.csproj` to add a
`<ProjectReference Include="..\Merchants\Merchants.csproj" />` — an
established pattern, not a new one: `Shipments.csproj` and `Cod.csproj`
already reference `Merchants.csproj` for exactly this reason (to use
`MerchantPermissionCatalog`/`RequireMerchantPermissionAnyCredential` in their
own merchant-facing routes).

### Not in scope this phase

- No update-role / deactivate endpoint for `MerchantStaffEndpoints` — only
  `POST` (invite) and `GET` (list) exist today. Out of scope, matching how
  prior phases parked adjacent gaps rather than scope-creeping; recorded in
  `docs/CLARIFICATIONS.md` as a follow-up, not fixed here.
- No new `Invoice` entity, no PDF-generation service (per resolved design
  question 1 above).
- No branch auto-resolution service (per resolved design question 2 above).
- No changes to the already-complete merchant `Shipments`/`Pickups`/`Cod`
  dual-auth routes from Phase 12 — this phase only adds a UI in front of
  them.

## Frontend structure

A fully separate portal, parallel to (not nested inside) the existing
`(staff)` route group, per resolved design question 3.

### Session & routing

- New cookies `courier_merchant_access_token` / `courier_merchant_refresh_token`,
  new `src/web/src/lib/auth/merchantSession.ts` — line-for-line the same
  shape as `lib/auth/session.ts` (`setMerchantSessionCookies`,
  `clearMerchantSessionCookies`, `getValidMerchantAccessToken`), pointed at a
  new `merchantAuthService` (`src/web/src/services/merchantAuthService.ts`,
  same shape as `authService.ts`: `login`, `refresh`, `me` against
  `/api/v1/merchant/auth/{login,refresh,me}`).
- New BFF routes: `app/api/merchant-auth/{login,logout,refresh,session}/route.ts`
  — same bodies as their `app/api/auth/*` counterparts, importing from
  `merchantSession.ts`/`merchantAuthService.ts` instead.
- New route group `app/(merchant)/...` with its own `layout.tsx` rendering a
  new `MerchantAppShell` (`components/merchant-layout/MerchantAppShell.tsx`) —
  reuses the shadcn `Sidebar`/`SidebarProvider` primitives `AppShell` already
  wraps, but its own nav catalog and its own `UserMenu`-equivalent (merchant
  name/email, logout via `/api/merchant-auth/logout`). Not literally the same
  `AppShell` component, since that component's header/sidebar pieces
  (`BranchSwitcher`, staff `UserMenu`) are wired to staff `useSession` and
  organization-branch concepts that don't exist on this side.
- New `middleware.ts` matcher entry (or a second middleware function keyed
  off path prefix, since Next.js supports one `middleware.ts` per app but
  multiple matcher patterns) — `/merchant/:path*` redirects to
  `/merchant-login` when neither merchant cookie is present, same UX-only
  reasoning the existing staff matcher comment already documents.
- New `/merchant-login` page + `features/merchant-auth/MerchantLoginForm.tsx`
  (same shape as `LoginForm.tsx`, posting to `/api/merchant-auth/login`).
- New `features/merchant-auth/hooks/useMerchantSession.ts` (same shape as
  `useSession.ts`, fetching `/api/merchant-auth/session`) and
  `useMerchantPermission.ts` (same shape as `usePermission.ts`, checking
  `session.permissions.includes(...)` — no branch-scope parameter, since
  merchants have no branch concept).

### Nav

`components/merchant-layout/MerchantNavMain.tsx` (same flat-list pattern as
`nav-main.tsx`, gated by `useMerchantPermission` instead of `usePermission`):

| href | label | permission | icon |
|---|---|---|---|
| `/merchant/dashboard` | Dashboard | *(always visible)* | `LayoutDashboard` |
| `/merchant/shipments` | Shipments | `ShipmentRead`* | `Package` |
| `/merchant/pickups` | Pickups | `merchant.pickups.read` | `Truck` |
| `/merchant/cod` | COD & Invoices | `merchant.cod.read` | `Wallet` |
| `/merchant/kyc` | KYC | `merchant.kyc.upload` | `ShieldCheck` |
| `/merchant/staff` | Staff | `merchant.staff.manage` | `Users` |
| `/merchant/api-keys` | API Keys | `merchant.apikeys.manage` | `KeyRound` |

\* `MerchantPermissionCatalog.ShipmentRead` is the literal string
`"shipment.read"` (shared key-name-but-separate-catalog with staff, per
`MerchantPermissionCatalog`'s own doc comment) — the nav item's permission
check uses that literal string directly (`nav-main.tsx` inlines its own
permission strings the same way, matched here for consistency).

### Screens (Sheet-over-pages for every detail/create, per the standing convention)

**Dashboard** (`/merchant/dashboard`) — small client-side aggregation over
already-fetched list data (today's shipments by status from
`GET /api/v1/shipments`, COD pending/settled summary from
`GET /api/v1/cod/settlements`), reusing the `reports` feature's existing
stat-tile component. No new backend read.

**Shipments** (`/merchant/shipments`) — new `features/merchant-shipments/`:
- List: `DataTable` against `GET /api/v1/shipments` (already merchant-scoped
  server-side via `HasReadAccessAsync`'s `GetMerchantId()` branch — no
  merchant-id filter needed client-side). Columns: AWB, Recipient, Destination,
  Status (`StatusBadge namespace="Shipment"`, existing config), COD Amount,
  Created At.
- Booking Sheet (`ShipmentCreateSheet`-equivalent, reusing `ItemsField`,
  `GeographyPicker` with `idPrefix="recipient"`/`"sender"`, and a new
  `useMerchantShipmentQuote` hook hitting the now-dual-auth
  `POST /api/v1/pricing/quote` for a live preview): Origin/Destination branch
  Select (new `useMerchantBranches()` read-only list hook against the new
  `GET /api/v1/merchant/branches` from Backend changes §4), service type, weight/dimensions, COD amount,
  insurance toggle, recipient details + `GeographyPicker`, sender details
  (defaulted from the merchant's own profile address where available, still
  editable), items → `POST /api/v1/shipments` (`MerchantId` omitted — backend
  forces it to the caller's own id per the DTO's own doc comment).
- Detail Sheet: status, tracking events (reuses `ShipmentDetailSheet`'s
  existing tracking-event list rendering pattern), Cancel action
  (`merchant.shipments.cancel` i.e. `"shipment.cancel"`) →
  `POST /api/v1/shipments/{id}/cancel`.
- Also serves the roadmap entry's "tracking" scope item — the detail Sheet's
  tracking-event list *is* the merchant's tracking view; no separate public
  `/track/{awb}` work here (that's Phase F11).

**Pickups** (`/merchant/pickups`) — new `features/merchant-pickups/`: list
against `GET /api/v1/pickups` (already merchant-scoped), create Sheet
(multi-select AWB picker sourced from the merchant's own un-picked-up
shipments already in the Shipments list query cache, requested date via
`DatePicker`, notes) → `POST /api/v1/pickups`, cancel action on pending rows
→ `POST /api/v1/pickups/{id}/cancel`.

**COD & Invoices** (`/merchant/cod`) — new `features/merchant-cod/`: list
against `GET /api/v1/cod/settlements` (already merchant-scoped). Detail Sheet
doubles as the invoice view (resolved design question 1): period, itemized
table (Shipment, COD Amount, Courier Fee, Net Payable — same columns
`MerchantSettlementItemResponse` already carries), total payable,
`StatusBadge namespace="MerchantSettlement"` (reuses F7's existing token
mapping), settled date. A `@media print` stylesheet on the Sheet's content
(hides nav chrome, prints just the itemized table) plus a "Download CSV"
button building a CSV client-side from the already-fetched JSON (same
`CsvWriter`-equivalent escaping discipline as the backend's own exporter —
reuse the frontend's existing CSV-building logic from
`ExportReportsCard`/F8 if it has one, otherwise a small local helper with the
same RFC 4180 + formula-injection-guard rules `CsvWriter.EscapeField`
documents server-side).

**KYC** (`/merchant/kyc`) — new `features/merchant-kyc/`: document list
against `GET /api/v1/merchant/kyc-documents`, upload Sheet (document-type
Select using the now-fixed `KycDocumentType` enum, file input restricted to
`application/pdf,image/jpeg,image/png` client-side matching the backend's own
allowlist, 10MB size warning matching the backend's own cap) → raw-body
`POST /api/v1/merchant/kyc-documents?documentType=...`. Each row shows a
`StatusBadge namespace="Kyc"` (new token mapping: `Pending` → `status-created`,
`Approved` → `status-delivered`, `Rejected` → an existing failure-toned
token, validated via the `dataviz` skill same as every prior phase's new
badge set).

**Staff** (`/merchant/staff`, Owner-only — `MerchantStaffEndpoints` is
already `RequireMerchantPermission("merchant.staff.manage")`, JWT-only same
carve-out as API keys) — new `features/merchant-staff/`: list against
`GET /api/v1/merchant/staff`, invite Sheet (email, password, full name) →
`POST /api/v1/merchant/staff`. No edit/deactivate UI (per Backend changes'
"Not in scope" — the endpoint doesn't exist).

**API Keys** (`/merchant/api-keys`, Owner-only, JWT-only self-escalation
carve-out already enforced server-side) — new `features/merchant-api-keys/`:
list against `GET /api/v1/merchant/api-keys` (masked — only `Prefix`/
`Scopes`/`IsActive`/timestamps, matching `ApiKeyResponse`'s shape, never the
secret), create Sheet (optional scopes multi-select, defaulting to "every
permission my role grants" per the backend DTO's own doc comment) →
`POST /api/v1/merchant/api-keys`, whose response's `PlaintextKey` is shown
**exactly once** in a copy-to-clipboard callout with an explicit "you won't
be able to see this again" warning (same one-time-reveal UX pattern the
backend's own doc comments describe for both `ApiKey` and
`WebhookSubscription.Secret`), revoke action per row →
`DELETE /api/v1/merchant/api-keys/{id}`.

### Not building this phase (roadmap says "if F5 approximation proves
insufficient" for F12, same YAGNI reasoning applies here)

- No merchant-facing webhook-subscription UI, even though
  `MerchantPermissionCatalog.WebhooksManage` and the backend
  `WebhooksEndpoints` already support it — the roadmap entry's named scope is
  "booking, tracking, COD settlements, invoices, API key management," and
  webhook subscriptions are a developer-integration feature more naturally
  paired with the API-key screen's audience but not named in scope. Flagged
  as a candidate follow-up in `docs/CLARIFICATIONS.md`, not built here.

## Testing

- **Backend**: unit tests for `MerchantAuthService.GetMeAsync` (happy path,
  inactive-user rejection, inactive-merchant rejection, correct
  `Permissions` per role), a unit test for the enum-serialization fix (round-
  trip a `KycDocumentType`/`MerchantUserRole` value through JSON), unit +
  integration tests for the dual-auth `pricing/quote` path proving a merchant
  JWT and a scoped API key can both reach it while an unscoped API key
  cannot (mirrors the existing `ShipmentsEndpoints` dual-auth test pattern),
  and a unit + integration test for `GET /api/v1/merchant/branches` proving
  it returns every active branch org-wide for a merchant caller (no
  accessible-branch-id narrowing) while still respecting tenant isolation
  (a second org's branches never appear) and rejecting an unscoped API key.
- **Frontend**: Vitest tests for `merchantSession.ts`'s refresh-on-expiry
  logic (mirrors any existing `session.ts` test), the merchant login form,
  `useMerchantPermission`, the booking Sheet's quote-preview wiring, the CSV
  export's escaping of a merchant/shipment name containing a leading `=`
  (formula-injection guard, client-side equivalent of the backend's own
  server-side rule), and the API-key create Sheet's one-time-reveal behavior
  (key is shown once, gone after Sheet close/reopen).
- **Manual E2E** against the real API/Postgres before the phase is called
  done — every prior phase's own pass found real bugs invisible to unit
  tests alone. Register a fresh org via the API, seed a branch + zone + rate
  card (existing test-fixture shapes), create a merchant + Owner user via the
  API, log into the new `/merchant-login`, book a shipment with a live quote
  preview, raise a pickup request bundling it, drive the shipment to
  delivery via the staff side (existing F5/F7 flows) with a nonzero COD
  amount, create + settle a merchant settlement for the period from the
  staff COD screen, confirm the resulting invoice detail Sheet and CSV
  export show the correct itemized figures on the merchant side, upload a
  KYC document, invite a Staff-role merchant user and confirm their nav
  correctly hides the Staff/API Keys entries, and create + revoke an API
  key confirming the plaintext is shown exactly once.

## Definition of Done (CLAUDE.md §37, applied to this phase)

A merchant can log in through a session fully separate from staff, book a
shipment with a live price preview, track it through delivery, raise and
cancel pickup requests, view and export their COD settlements as invoices,
upload and track KYC documents, invite staff (Owner only), and manage API
keys (Owner only) — every screen permission-gated by the merchant's role via
the new `/me` endpoint; loading/error/empty states on every new screen;
backend + frontend tests passing; `docs/FRONTEND-ROADMAP.md` updated with a
completed F10 entry; build green; no regressions in F0–F9 or the existing
Phase 12 merchant API/tracking surface.
