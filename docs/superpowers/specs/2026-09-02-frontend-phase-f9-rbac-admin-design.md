# Frontend Phase F9 — RBAC/Admin — Design

**Date:** 2026-09-02
**Status:** Approved, ready for implementation planning.
**Roadmap entry:** `docs/FRONTEND-ROADMAP.md` Phase F9.

## Context

F0–F8 are complete (`docs/FRONTEND-ROADMAP.md`). Unlike every prior frontend
phase, F9's backend surface is almost entirely missing rather than mostly
complete-with-small-gaps: investigating `src/Modules/Identity/` before design
found only 5 endpoints exist today (`POST /api/v1/auth/register`, `POST
/api/v1/auth/login`, `POST /api/v1/auth/refresh`, `GET /api/v1/auth/me`, `GET
/api/v1/users`). There is no Role CRUD, no Permission-catalog read endpoint, no
RolePermission assignment, no UserBranch grant/list/revoke endpoint, and no
invite/deactivate — despite the domain model (`Role`, `Permission`, `UserRole`,
`UserBranch`) already carrying the shape needed for all of it, including
`UserBranch.ExpiresAt` for temporary grants. This phase's backend work is
therefore closer in size to a small backend phase than the usual "close 3-5
gaps" pattern F5–F8 followed — approved as a combined phase (backend tasks
first, frontend tasks after, one plan/worktree) rather than split into two
phases.

**One claim from initial investigation turned out to be wrong on closer
reading, corrected here:** `GET /api/v1/users` (`UsersEndpoints.cs`) has no
explicit `OrganizationId` filter in its query, which looked at first like the
same cross-tenant bug class hit in F2/F4/F5/F7. It is not — `AppDbContext.cs`
line 328 already registers a global `HasQueryFilter` on `User` scoped to
`_currentTenantProvider.OrganizationId`, so every `Set<User>()` query in an
HTTP request context is already tenant-scoped, the same "safe via the ambient
filter, but stylistically inconsistent with this module's own explicit-filter
convention elsewhere" situation Phase 10's and Phase 13's final reviews found
in a couple of read paths. `Role` and `UserRole` carry the identical ambient
filter (lines 329-330). This phase's real, concrete gaps in `/api/v1/users`
are: no pagination, and the response carries no role or branch-grant
information (`UserListItem` is `Id, Email, FullName, IsActive` only) — both
addressed below, but as ordinary feature gaps, not a security fix.

**`UserBranch.ExpiresAt` enforcement was also flagged as unverified and turned
out to already work.** `BranchAuthorizationChecker.GetAccessibleBranchIdsAsync`
(`Infrastructure/BranchAuthorizationChecker.cs` line 54-56) already filters
`ub.ExpiresAt == null || ub.ExpiresAt > now` when resolving a `SpecificBranches`
grant. No enforcement fix is needed; this phase adds a regression test pinning
the behavior (an expired grant is excluded) since nothing exercises this
specific branch of that method by name today.

## Backend additions (`src/Modules/Identity/`)

### Role CRUD + Permission catalog

New `Presentation/RolesEndpoints.cs`, all `role.manage`-gated:

- `GET /api/v1/roles` — org's own roles (custom + the 3 system-default ones
  seeded at registration) with each role's permission-key list, `IsSystemDefault`.
- `POST /api/v1/roles` — `{ name, permissionKeys[] }` → new custom `Role`
  (`OrganizationId` = caller's org, `IsSystemDefault = false`), joining the
  already-tracked `Permission` rows by key (same pattern `AuthService`'s
  seeding already uses — `Permission` rows are pre-seeded once at the platform
  level via `PermissionCatalog`, never created per-org).
- `PUT /api/v1/roles/{id}` — replace a custom role's `permissionKeys[]`
  wholesale (simplest correct semantics — no diffing). Rejects with 400 if
  `IsSystemDefault` is true.
- `DELETE /api/v1/roles/{id}` — rejects with 400 if `IsSystemDefault` is true,
  or if any `UserRole` row still references it (delete the assignment first).

New `GET /api/v1/permissions` — the full `PermissionCatalog.All` catalog,
grouped server-side by the token before the first `.` in each key (`shipment`,
`cod`, `manifest`, ...) so the frontend renders a grouped checkbox grid without
parsing dot-keys itself:

```csharp
public record PermissionGroupResponse(string Group, IReadOnlyList<PermissionItem> Permissions);
public record PermissionItem(string Key, string Description);
```

Gated on `role.manage` (only someone who can assign permissions needs to see
the catalog).

### Seed expansion to the full CLAUDE.md §6 role list

`AuthService.RegisterOrganizationAsync` currently seeds 3 roles
(`OrganizationOwner`, `DeliveryRider`, `PickupRider`). This phase expands that
to the org-scoped subset of CLAUDE.md §6's 17-role list, adding 11 new roles:
`OrganizationAdmin`, `OperationsManager`, `BranchManager`, `Dispatcher`,
`SortingStaff`, `CounterStaff`, `FinanceManager`, `FinanceStaff`,
`CustomerSupport`, `Auditor`, `HubStaff` — 14 seeded roles total, each with a
permission set derived from the module read/write verbs CLAUDE.md §6 already
lists per role family — e.g. `FinanceManager` gets the `cod.*` set plus
`report.read`, `BranchManager` gets branch-scoped operational permissions
(`shipment.*`, `manifest.*`, `rider.read`, `rider.assign`), `Auditor` gets only
the `*.read` + `audit.read` permissions. Two of CLAUDE.md §6's 17 are
deliberately excluded from this per-org seed: `SuperAdmin` is a platform-level
role with no `OrganizationId` (out of scope — no platform-admin UI exists or
is planned this phase), and `Merchant`/`MerchantStaff` are the existing
`MerchantUser`/API-key auth track (Phase 4/12), a separate identity space from
staff `User`/`Role` entirely. All 14 staff-side roles seed as
`IsSystemDefault = true`, matching the existing 3 — none of them become
editable/deletable through this phase's new role UI, only org-created custom
roles are.

### User invite (email-based, not admin-set-password)

New `Domain/UserInvite.cs`:

```csharp
public class UserInvite
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
```

Mirrors `RefreshToken`'s plaintext-token/hashed-storage shape exactly (a
32-byte random token, SHA-256 hashed at rest, plaintext only ever in the email
and the accept-invite request body).

`POST /api/v1/users/invite` (`user.create`-gated): `{ email, fullName, phone?,
roleId, branchId, expiresAt? }` → creates a `User` row with `IsActive = true`
but an unusable `PasswordHash` (hash of a fresh random GUID, same
never-matches-a-real-password technique `AuthService`'s `DummyPasswordHash`
already establishes), the requested `UserRole` (`BranchScope.SpecificBranches`)
and `UserBranch` (with the optional `expiresAt`), a `UserInvite` row, and
publishes a `user.invited` domain event via the existing `IOutboxWriter`
(Events module, same mechanism `Shipments`/`Ndr`/`Cod` already use) carrying
`{ UserId, RecipientEmail, InviteToken, FullName }` in its payload. This new
service (`Application/UserManagementService.cs`) is deliberately separate from
`UserProvisioningService` — that interface is narrowly scoped to Riders'
internal seeded-role/upfront-password provisioning (its own doc comment says
so) and stays untouched; this phase's invite flow needs an arbitrary
role/optional-expiry/no-upfront-password shape `IUserProvisioningService`
was never meant to cover.

`POST /api/v1/auth/accept-invite`: `{ token, password }` (no auth required —
same unauthenticated-endpoint class as login/register) → hashes the token,
looks up the matching un-accepted, unexpired `UserInvite`, sets the `User`'s
real `PasswordHash`, stamps `AcceptedAt`, and returns an `AuthResult` (access +
refresh token) so the new user lands signed in immediately, matching
`RegisterOrganizationAsync`'s own return shape.

**Notification pipeline gap this closes:** `NotificationDispatchService.
DispatchEventAsync` today only resolves a recipient by reading `MerchantId`
from the event payload and looking it up via `IMerchantContactLookupService`
— there is no path for an event whose payload already carries its own
recipient address. This phase adds one: before the existing `MerchantId`
branch, check for a top-level `RecipientEmail` payload property and use it
directly if present, skipping the merchant lookup entirely. This is additive
only (existing `MerchantId`-keyed events are untouched) and is the minimum
change needed for `user.invited` to be deliverable at all. `NotificationDefaultTemplates.
Email` gains `["user.invited"] = "You've been invited to join {OrganizationName}
on Courier. Set your password: {InviteUrl}"` (the `InviteUrl` is built by the
endpoint from the plaintext token plus the frontend's own base URL, same as
every other placeholder-substitution template already does — no new
templating capability needed).

### User deactivate/reactivate + list improvements

- `PATCH /api/v1/users/{id}/deactivate` / `PATCH /api/v1/users/{id}/reactivate`
  (`user.update`-gated) — toggle `User.IsActive`. Deactivating a user whose
  refresh token is still live must not let them mint a new access token;
  `AuthService.RefreshTokenAsync` already checks `user.IsActive` (line 188), so
  this is enforcement-already-exists, endpoint-missing, not a new check.
- `GET /api/v1/users` gains `page`/`pageSize` (matching every other paginated
  list endpoint's convention) and enriches `UserListItem` with `RoleName` and
  `BranchCount` (a cheap aggregate, not the full branch list — the per-user
  detail Sheet fetches the full grant list separately).

### `UserBranch` grant/list/revoke

- `GET /api/v1/users/{id}/branches` (`user.read`) — the user's current grants
  (`BranchId`, branch name via the existing `IBranchLookupService`, `GrantedAt`,
  `ExpiresAt`).
- `POST /api/v1/users/{id}/branches` (`user.update`) — `{ branchId, expiresAt?
  }`, rejects a duplicate active grant for the same branch.
- `DELETE /api/v1/users/{id}/branches/{branchId}` (`user.update`) — revokes
  (hard delete — `UserBranch` has no soft-delete/status field anywhere else in
  the codebase either).

### Enum fix

`Domain/BranchScope.cs` gets `[JsonConverter(typeof(JsonStringEnumConverter))]`
— the same recurring bug class hit in `BranchType` (F2), `ShipmentStatus`
(F3), `ManifestPurpose`/`ManifestStatus` (F4), the 5 Riders enums (F5), the 3
Ndr enums (F6), and the 3 Cod enums (F7). `BranchScope` is the one enum in
`UserRole`'s own response shape that would otherwise silently serialize as a
raw integer the moment this phase's role/user UI renders it.

## Frontend structure (`src/web/src/features/admin/`)

New `/admin` route (`src/web/src/app/(staff)/admin/page.tsx`), nav entry added
to `NAV_ITEMS` in `src/web/src/components/nav-main.tsx` gated on `role.manage`
OR `user.read` (visible if the user holds either — someone with only
`user.read` still needs to reach the Users tab). A plain button-row tab
switcher exactly like `/cod`'s (`AdminPage` owns `activeTab` state, no new
Tabs library), two tabs: **Roles** and **Users**. Branch grants are not a
third top-level tab — a grant only makes sense in the context of one user, so
they live inside `UserDetailSheet` as a sub-section, the same way F2's branch
detail Sheet composes `BranchShipments`/`BranchExceptions` rather than giving
them their own nav entries.

One shared `adminPanelStore.ts` (Zustand, mirrors `codPanelStore.ts`'s
discriminated-union-of-modes shape exactly):

```typescript
type AdminPanelState =
  | { mode: "closed" }
  | { mode: "role-create" }
  | { mode: "role-detail"; id: string }
  | { mode: "user-invite" }
  | { mode: "user-detail"; id: string };
```

### Roles tab

`RolesTab.tsx` — `DataTable` (Name, permission count, System/Custom badge) +
create button opening `RoleCreateSheet.tsx`. `RoleCreateSheet`/`RoleDetailSheet`
both render the permission catalog (`usePermissionCatalog()` hook fetching
`GET /api/permissions`) as a checkbox grid grouped by the
server-supplied `Group` field, one `<fieldset>` per group. `RoleDetailSheet`
opens read-only (all checkboxes disabled, no Delete button) whenever
`isSystemDefault` is true — matches `ReturnShipmentDetailSheet`'s established
pattern of a status-driven read-only mode rather than a separate component.

### Users tab

`UsersTab.tsx` — paginated `DataTable` (Email, FullName, Role, Active/Inactive
badge, branch count) + Invite button opening `UserInviteSheet.tsx` (email,
full name, phone, role picker sourced from `GET /api/roles`, branch picker,
optional expiry date picker for the initial grant — same `<input
type="date">` + UTC-anchoring-to-`T00:00:00Z`/`T23:59:59Z` pattern F7 and F8
both established for date fields reaching the API). `UserDetailSheet.tsx`
shows the user's record, an Activate/Deactivate toggle button (`user.update`-
gated), and a `UserBranchGrants` sub-section: list of current grants + an "Add
Grant" mini-form (branch picker + optional expiry date) + a remove action per
row.

### BFF routes

`src/web/src/app/api/roles/route.ts` (+ `[id]/route.ts`), `src/web/src/app/api/
permissions/route.ts`, `src/web/src/app/api/users/invite/route.ts`, `src/web/src/
app/api/users/[id]/deactivate/route.ts` (+ `/reactivate`), `src/web/src/app/api/
users/[id]/branches/route.ts` (+ `[branchId]/route.ts`) — all the same
pass-through-with-session-cookie pattern as `app/api/branches/route.ts` shown
below (no new pattern needed):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/roles");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

A separate unauthenticated BFF route, `src/web/src/app/api/auth/accept-invite/
route.ts`, proxies `POST /api/v1/auth/accept-invite` without requiring an
existing session (mirrors `app/api/auth/login/route.ts`'s no-session-required
shape) plus a new public page `src/web/src/app/accept-invite/page.tsx?token=...`
(outside the `(staff)` layout group, same reasoning as any future public-facing
route — no sidebar, no auth guard) with a simple set-password form.

## Testing

- **Backend unit**: `Role` domain rules (reject delete/edit of a
  `IsSystemDefault` role, reject delete of a still-assigned role),
  `UserInvite` token validation (expired, already-accepted, wrong token all
  rejected), the `BranchAuthorizationChecker` expired-grant regression test
  noted above, `NotificationDispatchService`'s new `RecipientEmail` branch.
- **Backend integration**: tenant/branch scoping on every new endpoint (this
  project's most-repeated bug class per `[[feedback-courier-sdd-workflow]]`) —
  a branch-restricted caller cannot list/grant/revoke another org's users or
  branches; `/api/v1/roles`/`/api/v1/permissions` never leak another org's
  custom roles.
- **Frontend**: Vitest tests for `adminPanelStore`'s mode transitions
  (mirroring `codPanelStore.test.ts`), `RoleCreateSheet`'s permission-grid
  submission shape, `UserInviteSheet`'s UTC-anchored expiry date handling.
- **Manual E2E** against the real API/Postgres before the phase is called
  done, following every prior phase's own practice: create a custom role,
  assign a subset of permissions, invite a user with that role, retrieve the
  invite email from the dev `mailhog` container (Phase 11's existing SMTP
  sandbox), accept the invite and confirm login works with the new password,
  grant that user a second branch with a short expiry, confirm access to that
  branch's data disappears once the grant's `ExpiresAt` passes, deactivate the
  user and confirm their refresh token stops working.

## Definition of Done (CLAUDE.md §37, applied to this phase)

An `OrganizationAdmin` can create a custom role with a chosen permission
subset, invite a new user by email into a role, and grant that user temporary
branch access — all through the UI, with the underlying backend enforcing
tenant/branch scope and permission checks server-side per CLAUDE.md §33.
System-default roles are never editable/deletable through the UI. Loading/
error/empty states on every new screen. Backend + frontend tests passing;
`docs/FRONTEND-ROADMAP.md` F9 entry updated with completion notes; build
green; no regressions in F0–F8.
