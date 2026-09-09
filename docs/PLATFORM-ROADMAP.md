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
landed on this branch; no task was skipped or descoped mid-plan.

## Known Limitations

Two gaps were identified during implementation itself — not anticipated by the original
design spec or plan, and not fixed within this phase's scope. Both are real and should be
treated as the first items considered when this track's next phase is scoped, not
forgotten background debt.

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
