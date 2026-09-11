# Platform Roadmap

Tracks platform-operator work (`SuperAdmin`, tenant lifecycle, billing) — a separate track
from `docs/ROADMAP.md`'s fixed backend Phase 0-14 list (CLAUDE.md §36) and
`docs/FRONTEND-ROADMAP.md`'s staff/merchant/rider phases. This work doesn't fit either
list: it operates *above* the tenant boundary both of those roadmaps take for granted.

## Phase P1 — SuperAdmin, Tenant Lifecycle, Billing

Design spec: `docs/superpowers/specs/2026-09-08-platform-super-admin-design.md`.
Plan: `docs/superpowers/plans/2026-09-08-platform-super-admin.md`.

Scope: a `SuperAdmin` identity (a `User` with `OrganizationId = null`, authenticated
through a dedicated `/api/v1/platform/auth/login`, never through the tenant RBAC
system); a new `Billing` module (`Plan`, `Subscription`, `Invoice`, `Payment` with
eSewa/Khalti checkout, invoice-then-pay-per-cycle, no true auto-recurring charge);
tenant lifecycle (suspend/reactivate/soft-delete/restore, auto-suspend on a 7-day-overdue
invoice); branch-count plan-limit enforcement (the only limit actually enforced this
phase — `MaxStaffUsers`/`MaxMonthlyShipments` are stored, not yet checked); a cross-org
platform dashboard (metrics + per-org drill-down); and time-boxed (30-minute)
impersonation with a dual-actor audit trail (`AuditLog.UserId` = the impersonated user,
`AuditLog.ImpersonatedByUserId` = the real `SuperAdmin`).

*Entry:* None — this was the first platform-track phase. *Exit criteria, all verified
working:* a `SuperAdmin` can suspend/reactivate/delete/restore an organization from
`/platform/organizations`; a `Plan` with a non-zero price generates a real `Invoice` each
cycle that an `OrganizationOwner` can pay via eSewa or Khalti from `/billing` (verified
live against the real eSewa UAT sandbox — a real redirect with the correct invoice
amount); an overdue invoice auto-suspends the org and a subsequent successful payment
auto-reactivates it (unless a `SuperAdmin` suspended it manually for cause); a
`SuperAdmin` can impersonate any tenant user for 30 minutes with every resulting action
attributed to both identities.

*Status: COMPLETE.* Implemented, reviewed, and merged across Tasks 1-19 of the plan
above; this document (Task 20) closes the phase, finishing 2026-09-09. All 20 tasks
landed on this branch; no task was skipped or descoped mid-plan. A final whole-branch
review after Task 20 (commits `a069dc4`, `fbeae75`) additionally found and fixed 2
Critical issues before merge — see "Findings from the final whole-branch review" below.

## Findings from the final whole-branch review

Once all 20 tasks were individually reviewed and merged onto this branch, a separate
final review looked at the branch as a whole (something no single task's review could
do) and found 2 Critical issues, both fixed before merge, plus a further set of
Important/Minor issues deliberately parked for the next phase rather than expanding
this one. Recording all of them here so they aren't lost now that the review session's
own working notes are gone.

**Fixed before merge (commits `a069dc4`, `fbeae75`):**

- Khalti payment verification (`KhaltiGatewayClient.VerifyAsync`) didn't bind the
  callback to the specific `Payment` being confirmed or check the paid amount — an
  org's own Owner could pay a cheap invoice via Khalti, then replay that payment's
  `pidx` against a different, more expensive invoice's callback to mark it paid for
  free. Fixed via a new `Payment.GatewayReference` column persisted at initiate time
  and checked (together with the amount) at verify time. eSewa was never vulnerable to
  this (its flow already binds `transaction_uuid` to the payment id under an HMAC).
- `SubscriptionService.GetPlanSummaryAsync`/`ListInvoicesAsync` were missing
  `.IgnoreQueryFilters()`, unlike their siblings in the same file — under Platform's
  null ambient tenant, this silently made the SuperAdmin org list's Plan column and the
  org-detail Sheet's Invoices section always empty in production. `Platform.Tests`'
  fakes hard-coded exactly this empty result, so no test caught it.
- Non-JSON gateway callback payloads (malformed input, HTML error pages from a gateway
  outage) crashed `Payment.RawVerificationPayload`'s `jsonb` write with a raw 500
  instead of a controlled `Failed` transition — now wrapped as valid JSON before write.
- The impersonation session cookie's `maxAge` was hardcoded to 15 minutes while the
  impersonation token itself lives 30 minutes and cannot be refreshed — the operator
  was bounced to `/login` at half the banner's advertised countdown. Now derived from
  the token's real expiry.
- `PlanService` had no `IAuditLogger` at all — plan creation/retirement (which sets the
  price every subscribed org is billed) was the one unaudited cross-tenant mutation in
  the whole phase. Now audited (`plan.created`/`plan.retired`, `IsCrossTenantAction: true`).
- There was no sign-out control anywhere in the platform frontend — the BFF logout
  route existed and worked but nothing in the UI ever called it. Added to `PlatformNav`.

**Parked for the next phase (not fixed here — small individually, but real):**

- `ReturnBaseUrl` (the redirect target after a gateway payment) is accepted from the
  client request body unvalidated and forwarded straight into the eSewa/Khalti redirect
  URLs (`BillingEndpoints.cs`, `PaymentService.InitiatePaymentAsync`). Should be
  derived server-side or checked against an allowlist.
- The four platform organization lifecycle endpoints
  (`PlatformOrganizationsEndpoints.cs`: suspend/reactivate/delete/restore) return a raw
  500 for an unknown organization id (`OrganizationAdminService.GetOrThrowAsync` throws
  a bare `InvalidOperationException`, uncaught) instead of a controlled 404 via the
  `PlatformValidationException` pattern the impersonation endpoint already uses.
- `Platform.Tests` is entirely fake-driven (hand-written fakes for
  `IOrganizationLookupService`/`IBillingLookupService`/`IBranchLookupService`/
  `IAuditLogReader`) — this is precisely what let the `IgnoreQueryFilters()` bug above
  ship undetected through 20 tasks of review. Any future method on these interfaces
  whose doc comment promises cross-tenant/null-ambient-tenant behavior should get at
  least one real-`AppDbContext` regression test (the pattern already exists:
  `OrganizationAdminServiceTests.GetAsync_WithNullAmbientTenant_ReturnsOrganization`,
  `SubscriptionServiceTests`' equivalents). `PlatformDashboardEndpointsTests` also
  currently asserts only `totalOrganizations >= 0`, which cannot fail.
- No reconciliation job exists for a payment whose browser never returns from the
  gateway (closed tab, lost connectivity, failed redirect) — the `Payment` sits in
  `Initiated` forever, and `OverdueInvoiceJob` will eventually auto-suspend a tenant
  who actually paid. Both gateways expose server-side status lookups the code already
  calls once; a periodic sweep over stale `Initiated` payments would close this.
- Suspend and Delete on the organization detail Sheet
  (`OrganizationDetailSheet.tsx`) are single-click with no confirmation dialog, and
  `useOrganizationLifecycleActions.ts` has no `onError` handler — a failed mutation is
  silent to the operator.
- `PlatformDashboardService.GetMetricsAsync`/`ListAllInvoicesAsync` both call
  `IOrganizationLookupService.ListAsync()`, which materializes every `Organization` row
  into memory just to compute counts or build a name-lookup dictionary — the same class
  of full-table-scan as Known Limitation #2 below, on two more endpoints.
- Smaller, lower-priority items noted but not tracked in detail here: `BranchService`'s
  plan-limit check counts *all* branches while the platform console's branch count
  shows only active ones (a mismatch, not a security issue); impersonation isn't
  blocked for a suspended/deleted organization (arguably fine — the actor is already a
  SuperAdmin — but undecided rather than deliberate); `EsewaGatewayClient`'s
  status-check JSON parse is still unguarded and its signature comparison isn't
  constant-time (this codebase already has a `CryptographicOperations.FixedTimeEquals`
  precedent elsewhere); `MerchantAuthService.RefreshTokenAsync` has no suspension check
  unlike the staff equivalent; `docs/DOMAIN-MODEL.md` still describes the pre-Billing
  schema and was not reconciled with this phase's new tables.

## Known Limitations

Two further gaps were identified during implementation itself — not anticipated by the
original design spec or plan, and not fixed within this phase's scope. Both are real and
should be treated as the first items considered when this track's next phase is scoped,
not forgotten background debt.

1. **No organization ever actually gets a `Plan` assigned.** `SubscriptionService`
   (Billing module) has a working, unit-tested `AssignPlanAsync(organizationId, planId)`
   method, and a `SuperAdmin` can create/list/retire `Plan`s end-to-end (Task 16). But
   `AssignPlanAsync` is called only from test code (`SubscriptionServiceTests`,
   `BranchesEndpointsTests`, `BillingEndpointsTests`) — no Presentation-layer endpoint and
   no frontend affordance ever calls it in a real request path. Every organization created
   through self-registration ends up with no `Subscription` at all. The rest of the
   billing machinery genuinely works (invoice generation, eSewa/Khalti payment, auto-
   suspend-on-overdue, auto-reactivate-on-payment — all verified live in Task 19), but all
   of it is unreachable in practice until something assigns a `Plan` to a `Subscription`
   for a real org. The next phase that touches billing should add this — most likely a
   `SuperAdmin` action on the org detail Sheet (`/platform/organizations`), or a
   default-plan auto-assignment at self-registration time. Until then, this phase's
   billing cycle is fully built but effectively dormant for every real tenant.

   **Update 2026-09-10 — closed.** See
   `docs/superpowers/specs/2026-09-10-billing-trial-plan-assignment-design.md` and
   `docs/superpowers/plans/2026-09-10-billing-trial-plan-assignment.md`. Every self-registered
   organization now starts a 7-day trial (`SubscriptionStatus.Trial`, no `Plan` yet) via
   `AuthService.RegisterOrganizationAsync` → `IOrganizationSubscriptionStarter` →
   `SubscriptionService.StartTrialAsync`. An `OrganizationOwner` can self-serve pick a `Plan`
   from `/billing` (`GET /api/v1/billing/plans`, `POST /api/v1/billing/plan`) at any time,
   before or after the trial ends. `TrialExpiryJob` flips an unconverted trial to
   `TrialExpired`, at which point `TrialEnforcementMiddleware` blocks non-GET requests with
   `402 TRIAL_EXPIRED` until a `Plan` is chosen — backend-enforced, not a frontend-only gate. A
   `SuperAdmin` can also assign/override any org's `Plan` directly from
   `/platform/organizations` (`POST /api/v1/platform/organizations/{id}/plan`), which is the
   path available for the organizations that pre-date this feature and have no `Subscription`
   at all (deliberately not backfilled — see the design spec's Open Questions for why).

2. **`GET /api/v1/platform/organizations` is an unpaginated N+1 query.**
   `OrganizationLifecycleService.ListOrganizationsAsync` (Tasks 9-10) loops over every
   organization and issues two extra queries per row (a plan-summary lookup and an
   active-branch count) — sequentially, not batched — and the endpoint takes no
   page/limit parameters at all. Flagged as a latency concern during Task 15's review,
   then measured concretely in Task 18's live verification against the shared dev
   database's accumulated 25,922 test organizations: **89-96 seconds per request.** A
   production deployment starting from zero organizations won't feel this immediately,
   but it degrades linearly with org count from day one and there is no pagination
   mechanism anywhere in this plan to fall back on. This needs a real fix (batched/joined
   query plus cursor or offset pagination on the endpoint and the `/platform/organizations`
   list UI) before the platform-operator org count grows past a few hundred.

## Later

Deliberately out of this phase's scope (see the design spec's non-goals, §3): enforcement
of `MaxStaffUsers`/`MaxMonthlyShipments`, any non-Nepal payment gateway, annual billing
cycles, hard-delete/purge of soft-deleted organizations, fine-grained platform-operator
permission tiers, and self-serve plan upgrade/downgrade by an `OrganizationOwner` (only a
`SuperAdmin` can change an org's assigned `Plan` this phase — modulo the gap above, since
today nothing changes it at all). A plan-assignment path (see Known Limitations #1) and
paginating the organizations list (see Known Limitations #2) should both be prioritized
early in whatever phase picks this track back up. Beyond those, this codebase also carries
a smaller, already-documented residual risk worth remembering: `PaymentService.
CompletePaymentAsync`'s call into `IOrganizationLookupService.ReactivateIfSystemSuspendedAsync`
and `OverdueInvoiceJob`'s calls into `SuspendForNonPaymentAsync` both run outside their own
module's `SaveChangesAsync`, so a crash between the two steps can leave an organization's
suspension state briefly inconsistent with its billing state — parked deliberately rather
than building a cross-module distributed-transaction mechanism this codebase doesn't have
yet. Any of these becomes its own future phase once there's a real need, following this
repo's standing brainstorm → spec → plan cycle — never speculatively.
