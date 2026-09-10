# Design: Trial-Based Plan Assignment

**Date:** 2026-09-10
**Status:** Approved for planning
**Track:** Platform (`docs/PLATFORM-ROADMAP.md`) — closes Known Limitation #1

## 1. Problem

`docs/PLATFORM-ROADMAP.md`'s Known Limitation #1: no organization ever actually gets a
`Plan` assigned to a `Subscription`. `Billing.Application.SubscriptionService.AssignPlanAsync`
is fully implemented and unit-tested, but is called only from test code
(`SubscriptionServiceTests`, `BranchesEndpointsTests`, `BillingEndpointsTests`). Every
self-registered organization ends up with no `Subscription` row at all, and the ~25,922
organizations already registered on the shared dev database (per Task 18's live verification)
are in the same state. The entire invoice/payment/auto-suspend machinery built in Platform
Phase P1 is unreachable in production as a result.

## 2. Goals

* Every newly self-registered organization starts a 7-day trial automatically, no
  `SuperAdmin` action required.
* When the trial ends without a chosen `Plan`, write access is backend-enforced-blocked
  (not just a frontend redirect) until an `OrganizationOwner` picks one.
* An `OrganizationOwner` can self-serve pick/change their org's `Plan` (new capability —
  today only a `SuperAdmin` can call `AssignPlanAsync`, via `PlatformPlansEndpoints`'
  `PlanService`, which manages `Plan` CRUD, not `Subscription` assignment).
* A `SuperAdmin` can also assign/override any org's `Plan` directly, which is what fixes the
  already-registered orgs with no `Subscription` today.

## 3. Non-Goals

* Enforcing `Plan.MaxStaffUsers`/`MaxMonthlyShipments` (still deferred, unrelated gap).
* Annual billing cycles, non-Nepal gateways, hard-delete/purge (all already out of scope per
  `PLATFORM-ROADMAP.md` §Later).
* A payment-reconciliation sweep job for stuck `Initiated` payments (a separate, already-known
  gap — not touched by this change).
* Paginating `/platform/organizations` (a separate known gap, tracked independently).
* Self-serve *cancellation* of a `Subscription` — out of scope; an org that never picks a plan
  simply writes-blocks after the trial, there is no separate cancel flow to build.

## 4. Domain Model Changes

`src/Modules/Billing/Domain/SubscriptionStatus.cs` — append two new members (never insert
mid-list — this codebase has hit the "integer-persisted enum reordering" bug three times
already; `CLARIFICATIONS.md` entry documents the append-only rule):

```csharp
public enum SubscriptionStatus
{
    Active = 0,
    PastDue = 1,
    Cancelled = 2,
    Trial = 3,
    TrialExpired = 4
}
```

`src/Modules/Billing/Domain/Subscription.cs`:

* `PlanId` becomes `Guid?` (nullable). A trial subscription has no committed Plan.
* New `DateTimeOffset? TrialEndsAt`.

Both existing inner-join lookups already handle a null `PlanId` correctly as-is, with no
code change needed:

* `SubscriptionService.GetMaxBranchesAsync` — inner join on `PlanId` yields no row when
  `PlanId` is null → returns `null` → existing "no limit" convention (`IBillingLookupService`'s
  own doc comment already states this is the fail-open default).
* `SubscriptionService.GetPlanSummaryAsync` — same inner join, returns `null` → the existing
  `GET /api/v1/billing/plan` endpoint 404s. This is not good enough for the trial UI (see §6.3)
  so a new endpoint is added rather than changing this one's contract for its existing callers.

A new EF Core migration is required for the enum column values (no schema change needed for a
plain `int` column, but `PlanId`'s FK constraint must be relaxed to nullable, and
`TrialEndsAt` is a new column).

## 5. Registration Wiring

`Identity.Application.AuthService.RegisterOrganizationAsync` needs to start a trial, but
`Identity` cannot take a project reference to `Billing` — `Billing.csproj` already references
`Identity.csproj` (for `Organization`), so the reverse edge would cycle. This is the exact
shape already solved once in this codebase for `IBranchPlanLimitProvider`
(`Identity -> Branches`, `Billing -> Identity`, so `Branches` can't reference `Billing`
either — bridged today by `src/Api/BranchPlanLimitAdapter.cs`).

New pieces, same shape:

* `Identity.Application.IOrganizationSubscriptionStarter` — one method:
  `Task StartTrialAsync(Guid organizationId, CancellationToken cancellationToken = default)`.
* `AuthService.RegisterOrganizationAsync` takes this as a constructor dependency and calls it
  once, after the `Organization` row is added to the tracked change set (same transaction /
  `SaveChangesAsync` as the rest of registration — no separate round trip, no partial-failure
  window).
* `Billing.Application.SubscriptionService` gains `StartTrialAsync(organizationId)`: creates a
  `Subscription` with `Status = Trial`, `PlanId = null`, `TrialEndsAt = UtcNow.AddDays(7)`,
  `CurrentCycleStart = UtcNow`, `CurrentCycleEnd = UtcNow.AddMonths(1)` (cycle fields carried
  along for schema consistency; `InvoiceGenerationJob` only ever touches `Status == Active`
  rows, so they are inert until a real Plan is chosen).
* `src/Api/OrganizationSubscriptionStarterAdapter.cs` (new, same shape as
  `BranchPlanLimitAdapter.cs`) implements `IOrganizationSubscriptionStarter` by calling
  `SubscriptionService.StartTrialAsync`, wired in `Program.cs` DI next to the existing adapter
  registration.

No dependency on `Plan.IsDefault` at all — trial start needs no `Plan` row to exist, so a
fresh platform install (before any `SuperAdmin` has created a `Plan`) works unmodified.
`Plan.IsDefault` remains meaningful as which Plan is pre-highlighted/recommended on the
plan-picker UI (§6.3) — not repurposed, not wasted.

## 6. Self-Serve Plan Selection

### 6.1 New read endpoint

`GET /api/v1/billing/plans` (`Billing.Presentation.BillingEndpoints`, permission
`org.billing.manage`, same permission the rest of `/billing` already uses) — lists active
Plans (`Id, Name, Slug, MonthlyPriceNpr, MaxBranches, MaxStaffUsers, MaxMonthlyShipments,
IsDefault`) via a new `PlanService.ListActivePlansAsync` (staff-safe subset of the existing
`ListPlansAsync`, which is `SuperAdmin`-only and may include retired plans — do not reuse the
`SuperAdmin` method directly for a staff-facing route).

### 6.2 New write endpoint

`POST /api/v1/billing/plan` (`{ PlanId: Guid }`, same permission) calls the existing,
unmodified `SubscriptionService.AssignPlanAsync(tenantProvider.OrganizationId, request.PlanId)`
— the one method this whole gap is about, finally reachable from a real request. No change to
`AssignPlanAsync` itself: it already creates-or-updates the `Subscription`, sets
`Status = Active`, and (since our trial row already exists from §5) takes the `existing is not
null` branch, leaving `CurrentCycleStart/End` as originally set at trial creation — so a plan
chosen mid-trial doesn't reset the billing cycle clock.

### 6.3 Subscription status endpoint (new)

`GET /api/v1/billing/subscription` — returns `{ Status, TrialEndsAt, Plan: PlanSummary? }`
even when `PlanId` is null, so the frontend can render "N days left in your trial" /
"Trial expired — choose a plan" instead of the existing `GET /billing/plan`'s 404-on-no-plan
contract, which is left unchanged for its existing callers. Backed by a new
`SubscriptionService.GetStatusAsync(organizationId)` that reads the `Subscription` row
directly (no inner join), separately fetching the `Plan` only if `PlanId` is not null.

## 7. Trial Expiry + Enforcement

### 7.1 `TrialExpiryJob`

New Hangfire recurring job (`Billing.Application.TrialExpiryJob`), registered in `Program.cs`
next to the existing `OverdueInvoiceJob`/`InvoiceGenerationJob` registrations, same
cross-tenant shape (`IgnoreQueryFilters()`, no ambient tenant — this is the third job in this
codebase to follow that established pattern). Each run: `Subscription`s where
`Status == Trial && TrialEndsAt <= now` flip to `Status = TrialExpired`.

### 7.2 `TrialEnforcementMiddleware`

New middleware (`src/Api/Middleware/TrialEnforcementMiddleware.cs`), registered in
`Program.cs` after `UseAuthentication()`/`UseAuthorization()`, alongside
`ApiKeyRequestLoggingMiddleware`/`IdempotencyMiddleware` (same position in the pipeline —
tenant identity is already resolved by this point regardless of auth scheme, staff JWT or
merchant API key, since both populate `ICurrentTenantProvider`).

Logic:

1. Skip if `HttpMethods.IsGet/IsHead/IsOptions(request.Method)`.
2. Skip if the path starts with `/api/v1/auth`, `/api/v1/billing`, `/api/v1/platform`,
   `/track`, `/health`, `/swagger`, `/hangfire` (exact prefix list mirrors
   `IdempotencyMiddleware`'s existing `ClassifyRoute` style — a small static allowlist, not a
   config file).
3. Read `ICurrentTenantProvider.OrganizationId`; skip if null (unauthenticated, `SuperAdmin`,
   or a route this middleware shouldn't touch).
4. Look up the org's subscription status via a new `IBillingLookupService.IsWriteBlockedAsync
   (organizationId)` (added to the existing narrow lookup interface, implemented by
   `SubscriptionService`, `IgnoreQueryFilters()` per that interface's established dual-use
   convention). Returns `true` only when `Status == TrialExpired`.
5. If blocked, short-circuit with `402 Payment Required`,
   `{ error: { code: "TRIAL_EXPIRED", message: "Your trial has ended. Choose a plan to continue." } }`.

This is backend-enforced per CLAUDE.md §33 ("never trust... from frontend") — the frontend
redirect in §8 is a UX convenience on top of this, not the enforcement boundary itself.

## 8. SuperAdmin Manual Override

`OrganizationDetailSheet.tsx` (`src/web/src/features/platform-organizations/`) gains an
"Assign Plan" action — a `Select` of active Plans (via a new
`GET /api/v1/platform/plans` reuse, already `SuperAdmin`-gated and already lists active plans)
calling a new `POST /api/v1/platform/organizations/{id}/plan` (`PlatformOrganizationsEndpoints`,
`RequireSuperAdmin()`) that calls `SubscriptionService.AssignPlanAsync` directly — `Platform`
already takes a full project reference to `Billing` (see `PlatformPlansEndpoints`'s own
comment: "Plan CRUD is Platform's own admin surface over a module it already fully
references"), so this needs no adapter, unlike §5's Identity-side wiring.

This is what fixes the ~25,922 already-registered organizations sitting with no `Subscription`
today — they never went through the new trial-start code path in §5, so they need a
one-time-per-org manual assignment (or a follow-up backfill script, left to the implementation
plan to decide — see Open Questions).

## 9. Frontend

* `src/web/src/features/billing/` — new `useSubscriptionStatus` hook over §6.3's endpoint;
  `/billing` page (`app/(staff)/billing/page.tsx`) shows a trial countdown banner
  ("N days left in your trial") when `Status === "Trial"`, and a plan-picker (cards or a
  table over §6.1's list, each with a "Choose this plan" button calling §6.2) when
  `Status === "Trial"` or `"TrialExpired"`.
* A new `app/(staff)/billing/choose-plan/page.tsx` (or reuse `/billing` itself — implementation
  plan to decide) is where the app redirects to.
* Global redirect: the shared BFF error-handling path (already normalizes backend errors, per
  `docs/ARCHITECTURE.md`'s API client requirements) recognizes `TRIAL_EXPIRED` and redirects
  to the plan page — this touches the one shared error-normalization point, not every call
  site individually.
* `OrganizationDetailSheet.tsx` — new "Assign Plan" `Select` + mutation hook
  (`useAssignOrganizationPlan`, mirroring `useOrganizationLifecycleActions.ts`'s existing
  shape), per §8.

## 10. Testing

* `Billing.Tests`: `SubscriptionServiceTests` gains `StartTrialAsync`/`GetStatusAsync` cases;
  new `TrialExpiryJobTests` (mirrors `OverdueInvoiceJobTests`' existing shape).
* `Api.Tests`: new `TrialEnforcementMiddlewareTests` (or a dedicated
  `BillingEndpointsTests` addition) proving a `TrialExpired` org can still `GET`/log in/pay an
  invoice/call `POST /billing/plan`, but a `POST /api/v1/shipments` from that org returns 402
  with `TRIAL_EXPIRED`; a live end-to-end test: register → trial Subscription exists → force
  `TrialEndsAt` into the past → run `TrialExpiryJob` → write blocked → `POST /billing/plan` →
  write unblocked.
* `Identity.Tests`: `AuthServiceTests`' existing `RegisterOrganizationAsync` test(s) gain an
  assertion that a `Trial` subscription now exists post-registration (via a fake
  `IOrganizationSubscriptionStarter`, matching how other cross-module dependencies are already
  faked in this test project).
* `Platform.Tests`: new `PlatformOrganizationsEndpointsTests` case for the new assign-plan
  endpoint — per the `PLATFORM-ROADMAP.md` parked lesson ("`Platform.Tests` is entirely
  fake-driven... let the `IgnoreQueryFilters()` bug ship undetected"), this one should include
  at least one real-`AppDbContext` regression test, not only a hand-written fake.
* Frontend: Vitest coverage for the trial banner, plan-picker, and the `TRIAL_EXPIRED` redirect
  path; a manual E2E pass (this project's established practice — every phase's real bugs have
  been found by live E2E, not unit tests alone).

## 11. Open Questions for the Implementation Plan

* **Backfill for existing orgs:** should the implementation plan include a one-off script/
  migration data-fix to put the ~25,922 already-registered orgs into a `Trial` state (so they
  get the same 7-day grace + soft-lock treatment), or leave them with no `Subscription` at all
  (current behavior — unlimited, unblocked) until a `SuperAdmin` manually assigns a Plan per
  §8? Recommendation: leave them alone (no backfill) — their current unlimited/unblocked
  behavior is not a regression, and retroactively starting a clock on production orgs that
  didn't ask for one risks locking out real, paying-adjacent customers by surprise. A
  `SuperAdmin` can assign a Plan proactively via §8 whenever the business decides to.
* **Grace/reminder before hard 402:** no in-app warning banner countdown is specified beyond
  "N days left" on the `/billing` page itself — no email/SMS trial-ending reminder is in scope
  (Notifications' template gap is a separate known limitation, not reopened here).
* **Exact route for the plan-picker page** (`/billing` itself vs. a dedicated
  `/billing/choose-plan`) — left to the implementation plan/task breakdown.
