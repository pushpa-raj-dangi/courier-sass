# Platform Super Admin — Design Spec

## 1. Context

`docs/CLARIFICATIONS.md` #5 records a one-paragraph decision that `SuperAdmin`
is a platform-level role (`UserRole.OrganizationId = null`) for tenant
provisioning, billing/subscription management, and emergency support access —
made when Phase 1 resolved the RBAC role list's ambiguities. Nothing was ever
built against it: no `SuperAdmin` account can be created or logged in, no
subscription/billing entity exists anywhere in the domain model despite
CLAUDE.md §5 referencing "unlimited branches depending on subscription
limits," no tenant suspend/delete tooling exists, and `src/web` has no
platform-admin surface.

Inspection for this spec found `Identity.Domain.Organization` already carries
two stub fields nobody wires up: `SubscriptionPlan` (a free-text string,
default `"Free"`) and `IsActive` (bool, default `true`, never read anywhere —
grepped the whole of `src/` and it is written once at creation and never
checked again, not even at login). `org.billing.manage`,
`org.ownership.transfer`, and `org.delete` are seeded permission rows with no
code path anywhere that checks them.

Also found: `User.OrganizationId` and `Role.OrganizationId` are already
nullable (`Guid?`), but `UserRole.OrganizationId` is a non-nullable `Guid`,
and every tenant query filter in `AppDbContext` is written as
`_currentTenantProvider.OrganizationId != null && x.OrganizationId ==
_currentTenantProvider.OrganizationId` — a null ambient tenant ID hides every
row rather than exposing all of them (the exact gotcha `CLARIFICATIONS.md`
#12 already documents for background-job contexts). A platform-level identity
that tried to reuse the normal `UserRole`/permission-catalog RBAC path would
either need a schema change to a tenant-isolation-critical table or would see
nothing through the ambient filter. This spec avoids both by not routing
`SuperAdmin` through `UserRole` at all (§3).

This does not fit inside any of CLAUDE.md §36's fixed backend phases (0–14)
or `docs/FRONTEND-ROADMAP.md`'s phases — it is tracked as its own roadmap,
`docs/PLATFORM-ROADMAP.md` (new file, §11).

## 2. Goals / Exit Criteria

- A platform operator can log in as `SuperAdmin` (bootstrapped once from
  config, never via any public endpoint) and, from a `/platform/*` UI:
  - see every organization on the platform with its plan, subscription
    status, and branch count;
  - suspend, reactivate, soft-delete, and restore an organization;
  - define `Plan`s (price, branch/user/shipment limits) and assign one to
    an org;
  - see every org's invoice history and payment status;
  - impersonate a specific user in a specific org for a time-boxed window,
    with every action taken while impersonating attributed to both
    identities in `AuditLog`.
- A newly self-registered org is auto-assigned the platform's default
  (zero-price) `Plan` with an `Active` `Subscription` and no invoice.
- A `Plan` with a non-zero price generates a real `Invoice` each monthly
  cycle; the org's Owner can pay it via eSewa or Khalti; payment is
  confirmed only via a server-to-server verification call to the gateway,
  never trusted from the client redirect alone.
- An invoice unpaid 7 days past its due date auto-suspends the org; a
  suspended org's users/merchants are hard-blocked at login and API-key
  auth, with all live refresh tokens for that org revoked immediately.
- `BranchService.CreateAsync` enforces `Plan.MaxBranches` — the one live
  limit this phase actually enforces.
- Every cross-tenant action a `SuperAdmin` takes is audited with
  `IsCrossTenantAction = true`, filed against the *target* org (so that
  org's own `Auditor` can see it), with the acting `SuperAdmin`'s identity
  carried separately.

## 3. Non-Goals (explicit deferrals)

- **No enforcement of `MaxStaffUsers`/`MaxMonthlyShipments`** — both fields
  exist on `Plan` and are shown in the UI, but no code path blocks on them
  this phase (avoids the shipment-volume counter/reset-on-cycle complexity;
  a fast follow once a real limit is needed).
- **No Stripe or any non-Nepal gateway.** eSewa and Khalti only.
- **No true recurring auto-charge.** Neither gateway supports it well; the
  model is invoice-then-pay each cycle via a checkout link, not a silent
  automatic renewal charge.
- **No annual billing cycle** — monthly only.
- **No hard-delete/purge job** for soft-deleted organizations — archived
  data stays in Postgres indefinitely pending a future retention policy
  decision.
- **No fine-grained SuperAdmin permission catalog** — `IsSuperAdmin` is a
  single undifferentiated capability; if the platform ever needs multiple
  kinds of platform operator, that's a later phase's problem.
- **No self-serve plan upgrade/downgrade by the Owner** — only `SuperAdmin`
  can change an org's assigned `Plan` this phase, even though
  `org.billing.manage` is wired to letting the Owner *view* their plan/pay
  invoices.
- **No PDF invoice generation, no email receipt beyond the existing
  notification pipeline's plain-text templates.**

## 4. Module Structure

Two new modules, following this codebase's established
Domain/Application/Infrastructure/Presentation shape:

- **`Billing`** — owns `Plan`, `Subscription`, `Invoice`, `Payment`. No
  project reference to `Identity`, `Shipments`, or any other module's
  `DbSet` — every fact it needs about an org (does it exist, is it active)
  comes in as a parameter from its caller, mirroring `Cod`'s "zero project
  reference to Shipments" pattern from Phase 10. Owns the eSewa/Khalti
  gateway clients and their webhook/callback verification.
- **`Platform`** — owns nothing but the `SuperAdmin` auth surface,
  org-lifecycle actions (suspend/restore/soft-delete), the cross-org
  dashboard read models, and impersonation token issuance. Reads `Billing`
  through a new narrow `Billing.Application.IBillingLookupService`
  (`GetPlanSummaryAsync(organizationId)`, `GetMaxBranchesAsync
  (organizationId)`) — the same shape as `IBranchLookupService`,
  `IMerchantLookupService`, and the five Phase-13 `*ReportingLookupService`
  interfaces. Reads `Identity` through a new narrow
  `Identity.Application.IOrganizationLookupService` (list/get org rows,
  suspend/restore, revoke an org's refresh tokens) rather than a direct
  `DbSet<Organization>` reference.

`Identity` itself gains: `User.IsSuperAdmin` (bool, default false),
`Organization.DeletedAt` (nullable), `Organization.SuspendedBySystem` (bool,
default false — distinguishes an auto-suspension for non-payment from a
`SuperAdmin`'s manual suspend, so a later successful payment knows whether
it's safe to auto-reactivate, §8), and `AuditLog.IsCrossTenantAction` (bool)
+ `AuditLog.ImpersonatedByUserId` (nullable). `Organization
.SubscriptionPlan` (the free-text stub) is dropped in favor of `Billing`'s
real `Subscription` row — nothing reads the string field today so this is a
clean removal, not a migration of live data.

## 5. Domain Model

```
Plan
  Id, Name, Slug, MonthlyPriceNpr (decimal, 0 = free tier),
  MaxBranches (int?, null = unlimited), MaxStaffUsers (int?, stored only),
  MaxMonthlyShipments (int?, stored only), IsDefault (bool, exactly one true
  at a time), IsActive (bool — retiring a Plan stops new assignment, existing
  Subscriptions keep it), CreatedAt.

Subscription
  Id, OrganizationId, PlanId, Status (Active | PastDue | Cancelled),
  CurrentCycleStart, CurrentCycleEnd, CreatedAt, UpdatedAt.
  One row per organization (not versioned/append-only like RateCard — this
  is live operational state, not a priced-at-a-point-in-time snapshot).

Invoice
  Id, OrganizationId, SubscriptionId, PlanId, PlanNameSnapshot,
  AmountNprSnapshot, CycleStart, CycleEnd, Status (Pending | Paid | Overdue |
  Cancelled), DueAt, PaidAt, CreatedAt.
  PlanNameSnapshot/AmountNprSnapshot are copied at generation time — a later
  Plan price change never alters an already-generated Invoice, same
  principle as Shipment's locked-in pricing (Phase 6) and RateCard
  versioning (Phase 5).

Payment
  Id, InvoiceId, Gateway (Esewa | Khalti), GatewayTransactionId,
  AmountNpr, Status (Initiated | Succeeded | Failed), InitiatedAt,
  CompletedAt, RawVerificationPayload (jsonb — the gateway's own
  verification-call response, kept for audit/dispute resolution).
```

Tenant isolation: `Subscription`/`Invoice`/`Payment` (via `Invoice`) all
carry their own `OrganizationId` for direct filtering by a normal (non-
platform) caller — `Payment` isolates transitively through
`InvoiceId -> Invoice.OrganizationId`, the same "child table, no own
column" pattern already established for `ShipmentItem`/`ManifestShipment`.
`Plan` has no `OrganizationId` — it is platform-global, readable by any org
(to see their own plan's limits) but only writable by `SuperAdmin`.

## 6. SuperAdmin Auth

No `UserRole` involvement (§1). `User.IsSuperAdmin = true` plus a dedicated
`POST /api/v1/platform/auth/login` (email/password against `Identity`'s
existing password hashing) issuing a JWT with an `is_super_admin: true`
claim and no `organization_id` claim. A new `RequireSuperAdmin` minimal-API
filter (mirroring the existing `RequireBranchScopedPermission`/
`RequireMerchantPermission` filter shape) checks that claim on every
`Platform`/`Billing` endpoint — binary gate, no permission catalog.

**Bootstrap:** on API startup, a hosted-service check runs once: if no
`User` with `IsSuperAdmin = true` exists, create one from
`SUPERADMIN_EMAIL`/`SUPERADMIN_PASSWORD` environment variables (fail startup
loudly if `IsSuperAdmin` count is zero and the env vars are unset in a
non-Development environment — never start with an unreachable platform).
No public endpoint ever creates a `SuperAdmin`.

## 7. Org Lifecycle

- **Suspend** (`POST /platform/organizations/{id}/suspend`): sets
  `Organization.IsActive = false`; revokes every live `RefreshToken` (staff)
  and `MerchantRefreshToken` (merchant) row belonging to that org's users,
  so refresh is blocked immediately — an already-issued short-lived access
  token dies at its normal TTL, matching how JWTs already work everywhere
  else in this codebase (no new revocation-list infrastructure). Staff
  login (`AuthService.LoginAsync`), merchant login, and API-key auth all
  gain an `Organization.IsActive` check, returning a distinct
  `ORG_SUSPENDED` error rather than a generic 401 (the frontend needs to
  show "your organization has been suspended," not "wrong password").
  Audited as `IsCrossTenantAction = true` against the target org.
- **Reactivate**: sets `IsActive = true` and clears `SuspendedBySystem` back
  to `false` (a manual reactivation always supersedes whatever suspended the
  org). Same audit shape.
- **Soft-delete** (`org.delete`, `POST /platform/organizations/{id}/delete`):
  sets `DeletedAt`; implies suspended (also sets `IsActive = false` if not
  already). Shown as a distinct "Deleted" state in the platform dashboard,
  separate from "Suspended."
- **Restore**: clears `DeletedAt` only — does **not** auto-set
  `IsActive = true`. `SuperAdmin` must separately reactivate, a deliberate
  two-step against an accidental instant-back-online.
- **Branch limit**: `Branches.Application.BranchService.CreateAsync` gains
  a check — current branch count for the org vs.
  `IBillingLookupService.GetMaxBranchesAsync(organizationId)` — 400s with a
  `BRANCH_LIMIT_REACHED` code if at cap. `null` limit means unlimited.

## 8. Billing Cycle & Payment Flow

**Cycle generation:** a Hangfire recurring job (daily, mirroring Phase 11's
`OutboxPublisherJob` cadence pattern) finds every `Subscription` where
`CurrentCycleEnd <= today`, `Status == Active`, and its `Plan
.MonthlyPriceNpr > 0`: creates the next `Invoice` (snapshotting plan
name/price, `DueAt` = cycle end + a configurable grace window, default 7
days) and advances `CurrentCycleStart`/`CurrentCycleEnd` by one month.
Zero-price (free/default) plans are skipped entirely — no invoice ever
generated for them. The same job (or a second one run right after) flips
any `Invoice` past its `DueAt` + grace window that is still `Pending` to
`Overdue`, sets `Subscription.Status = PastDue`, and suspends the org —
audited with a system-actor marker (`ActingUserId = null`,
`IsCrossTenantAction = true`), distinct from a `SuperAdmin`-initiated
suspend.

**Payment:** the Owner (via `org.billing.manage`, finally consumed by real
code) lists their org's `Pending`/`Overdue` invoices and picks one to pay,
choosing eSewa or Khalti. Backend creates a `Payment` row
(`Status = Initiated`) and returns the gateway's redirect payload (eSewa's
signed form fields, or Khalti's `payment_url` from its initiate call —
exact request/response shapes are a planning-time lookup against each
gateway's current merchant API docs, not fixed by this spec). On the
browser's return to a BFF callback route, the backend performs a
**server-to-server verification call** to the gateway's own
transaction-lookup/status API before ever marking `Payment.Succeeded` /
`Invoice.Paid` — the client-side redirect alone is never trusted, since
both gateways' documented integration patterns exist specifically because a
redirect can be spoofed. `Invoice.Paid` sets `Subscription.Status = Active`
again if it had gone `PastDue`, and reactivates the org
(`Organization.IsActive = true`) only if `Organization.SuspendedBySystem`
is true (i.e. it was auto-suspended for non-payment) — a `SuperAdmin`'s
manual suspend leaves `SuspendedBySystem = false`, so paying an overdue
invoice never silently un-suspends an org a human operator suspended for an
unrelated cause. The auto-suspend job (§8) sets `SuspendedBySystem = true`
alongside `IsActive = false`; a manual `SuperAdmin` suspend (§7) leaves it
`false`.

## 9. Cross-Org Dashboard

`Platform` exposes read-only, `SuperAdmin`-gated endpoints:
`GET /platform/organizations` (list: name, plan, subscription status,
branch count, created date, suspended/deleted flags),
`GET /platform/organizations/{id}` (detail: users, branches, invoice
history, audit trail for that org), `GET /platform/dashboard` (org count,
active/suspended/deleted counts, MRR = sum of `Plan.MonthlyPriceNpr` across
`Active` subscriptions, overdue-invoice count). Every query here uses
`.IgnoreQueryFilters()` with an explicit `OrganizationId` predicate per row
where relevant — the established convention from `CLARIFICATIONS.md` #12 —
never the ambient tenant filter, which would silently return nothing for a
`SuperAdmin`'s null-org context.

## 10. Impersonation

`POST /platform/organizations/{orgId}/users/{userId}/impersonate` — verifies
the target user belongs to the target org, resolves that user's real
permissions/branch-scope exactly as a normal login would, and issues a
30-minute JWT carrying those claims plus `impersonated_by: <superAdminId>`
and `is_impersonation: true`. `AuditLog.ImpersonatedByUserId` (nullable) is
populated by the existing audit-writing path whenever a request's JWT
carries `is_impersonation` — every write made while impersonating is
attributed to both the on-behalf-of user (`UserId`, so that org's own audit
view reads naturally) and the real `SuperAdmin` (`ImpersonatedByUserId`).
No renewal endpoint — expiry forces a fresh, freshly-audited grant. The
frontend keeps the `SuperAdmin`'s own session token alongside the
impersonation token (not replacing it) so "End session" is a client-side
discard with no re-login needed, backed by a persistent banner showing the
impersonated identity and a live countdown to the token's `exp`.

## 11. Roadmap Tracking

New `docs/PLATFORM-ROADMAP.md`, structured like `docs/FRONTEND-ROADMAP.md` —
a separate track from the CLAUDE.md §36 backend phases, since none of this
fits the fixed 0–14 list. Phase P1 (this spec's full scope) is the only
phase defined for now; anything further (fine-grained platform-operator
roles, self-serve plan changes, a purge job) gets its own future spec.

## 12. Frontend

New `/platform/*` route tree in `src/web`, gated by a `SuperAdmin`-only
middleware check (a `platform_session` httpOnly cookie, separate from the
existing staff/merchant session cookies — a `SuperAdmin` is not an org
member and must not be reachable through `/login`). Own `/platform/login`
page. Pages, reusing the existing `DataTable`/`Sheet`/design-system
primitives per this project's standing convention:

- **Organizations** — `DataTable` list (name, plan, status badge, branch
  count), row click opens a detail `Sheet` with tabs for
  Users/Branches/Invoices/Audit and Suspend/Restore/Delete/Impersonate
  actions.
- **Plans** — list + create `Sheet` (name, price, limits); no delete (an
  `Invoice` can reference a retired `Plan`), only an `IsActive` toggle.
- **Invoices** — cross-org list, filterable by status.
- **Dashboard** — the metrics from §9 as stat tiles.
- A global **impersonation banner** (persistent, countdown timer, "End
  session") rendered in the app shell layout whenever an impersonation
  token is present — visible across the *tenant-facing* app while
  impersonating, not just under `/platform/*`.

The `org.billing.manage` permission (already seeded, never consumed) gets
its first real consumer here too: a new "Billing" tab under the existing
staff back-office (not `/platform/*`) where an `OrganizationOwner` sees
their org's current `Plan`, invoice history, and a "Pay Now" action for any
`Pending`/`Overdue` invoice.

## 13. Error Handling

- `ORG_SUSPENDED` — distinct error code at login/API-key auth for a
  suspended org, surfaced as a specific UI message rather than a generic
  auth failure.
- `BRANCH_LIMIT_REACHED` — 400 from `BranchService.CreateAsync` with the
  current count and the plan's limit in the response body, so the frontend
  can show "Upgrade your plan" rather than a bare validation error.
- Payment verification failure (gateway lookup says not-paid, or the
  lookup call itself fails/times out) leaves `Payment.Status = Failed` and
  `Invoice` unchanged — the Owner sees a "Payment not confirmed, try again"
  state and can retry; never optimistically marks paid on ambiguous
  gateway response.
- Impersonation against a nonexistent org/user, or a user who isn't
  actually a member of that org, 404s — never leaks which one is wrong to
  avoid an enumeration side-channel, matching the existing convention on
  auth-adjacent 404s elsewhere in this codebase.

## 14. Testing

- Unit: `Plan` limit resolution (`null` = unlimited), branch-count
  enforcement at exactly-at-cap and one-over, cycle-advancement math for
  the invoice-generation job, the overdue-grace-window boundary, and the
  `SuspendedBySystem` vs `SuspendedByAdmin` reactivation-on-payment
  distinction.
- Integration: a `SuperAdmin` JWT cannot be accepted by any tenant-scoped
  endpoint and a tenant JWT cannot be accepted by any `Platform`/`Billing`
  endpoint; a suspended org's staff/merchant login and API-key auth are all
  rejected with `ORG_SUSPENDED`; a `Task.WhenAll` concurrency test on the
  invoice-generation job proving it does not double-generate an invoice for
  the same cycle (mirroring the `RateCard`/`FinancialTransaction`
  concurrency-race tests from Phases 5/10).
- End-to-end: full flow over real HTTP — org self-registers onto the
  default plan, `SuperAdmin` reassigns it to a paid `Plan`, the invoice job
  generates an `Invoice`, a stubbed gateway verification confirms payment,
  `Invoice.Paid` and org stays active; a second scenario runs the invoice
  past its grace window unpaid and proves auto-suspension and login
  rejection; an impersonation scenario proves both `UserId` and
  `ImpersonatedByUserId` land on a write made while impersonating.
- eSewa/Khalti gateway calls are mocked/stubbed in all automated tests —
  no test suite makes a real call to either gateway's sandbox.
