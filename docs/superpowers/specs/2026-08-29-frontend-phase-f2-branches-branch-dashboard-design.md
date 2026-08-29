# Frontend Phase F2 — Branches + Branch Dashboard — Design Spec

**Date:** 2026-08-29
**Status:** Approved for implementation planning
**Scope:** `docs/FRONTEND-ROADMAP.md` Phase F2 — branch list/detail/create UI with geography chain
pickers, the branch dashboard (`BRANCH-OPERATIONS.md` §3), and wiring the branch switcher (built
inert in Phase F0) to real navigation.

## 1. Context

Phases F0/F1 shipped the design system, app shell (including a mobile nav drawer and a
`BranchSwitcher` that stores an `activeBranchId` in Zustand but nothing yet reads it), auth, and
the organization dashboard. This phase adds the `Branches` feature area the sidebar has linked to
since F0 (currently a 404).

Two real gaps surfaced while researching the backend contract, both resolved with the user before
design:

- **No generic branch-update endpoint.** `BranchesEndpoints.cs` exposes `POST /api/v1/branches`
  (create), `GET /api/v1/branches` (list, already server-side filtered to the caller's accessible
  branches via `GetAccessibleBranchIdsAsync` — no client-side re-filtering needed), `GET /api/v1/
  branches/{id}` (detail), and `PUT /api/v1/branches/{id}/delivery-zone` (the *only* field that can
  be changed after creation). The roadmap's "create/update" phrasing is resolved as: branch detail
  is read-only for every field except delivery zone, which uses the one real update endpoint that
  exists. The missing generic update endpoint is recorded in `CLARIFICATIONS.md` for a future
  backend pass — no UI is built for an operation the backend can't perform.
- **`RiderTaskLoad` (`{RiderId, OpenTaskCount}`) carries no display name.** Resolving one would
  need two more client-side joins (Riders list → Users list) just to label a task-load row. Per
  Phase F1's own precedent (branch/rider performance breakdowns deferred to Phase F8), the branch
  dashboard shows an aggregate `{riderCount, totalOpenTasks}` stat instead of a named per-rider
  list. No backend change, no extra joins.

## 2. Routing

```text
/branches           list — DataTable of accessible branches, permission branch.read
/branches/new        create form, permission branch.create
/branches/[id]        detail: read-only record fields + delivery-zone edit (branch.update,
                     branch-scoped) + the branch dashboard's operational sections
```

`BranchSwitcher` (`src/web/src/components/layout/BranchSwitcher.tsx`, built in Phase F0) currently
sets `activeBranchId` in `useUiStore` and nothing reads it. This phase makes selecting a branch
navigate to `/branches/{branchId}` in addition to setting the store value — giving the switcher a
real destination instead of inert state. `activeBranchId` itself stays for later phases (F3+) that
need "which branch is the user currently working in" for branch-scoped writes (e.g. booking a
shipment as a specific branch).

## 3. Branch list

A `DataTable` (Phase F0) fed by a new BFF route `GET /api/branches` (thin proxy, matching every
prior phase's pattern) and a `useBranches()` hook. Columns: Code, Name, Type (a plain text label,
not `StatusBadge` — branch type is a category, not a lifecycle status, so it doesn't belong in the
status-token system), District (resolved via a client-side lookup against the geography endpoints
— see §5), Active (a simple yes/no, not a colored badge — this is a boolean flag, not one of the
lifecycle states `StatusBadge` exists for). Row click navigates to `/branches/{id}`. A "New Branch"
button (permission `branch.create`, hidden otherwise via `usePermission`) navigates to
`/branches/new`.

## 4. Branch create

A form (React Hook Form + Zod, matching `LoginForm`'s established pattern) posting to a new BFF
route `POST /api/branches`. Fields, in the order a person filling this out would naturally reach
for them: Code, Name, Type (a `Select` — see §5 for why this component doesn't exist yet), the
geography chain (§5), Address Line, Phone (optional), Operating Hours (optional), Parent Branch
(optional `Select` populated from the branch list — supports the `ParentBranchId` hierarchy).
Latitude/Longitude exist on `CreateBranchRequest` but are not exposed in this phase's form — no
map picker exists yet and hand-typing raw coordinates is not a real workflow; deferred until a
map-based picker is worth building. On success, navigates to the new branch's `/branches/{id}`.

## 5. Geography cascading picker + the missing `Select` primitive

No shadcn `Select` exists in this codebase yet, and — same issue Phase F1 hit building `Dialog` —
the shadcn CLI's current version prompts interactively for a component library (Base UI / React
Aria / Radix UI) with no non-interactive flag this harness can answer. `Select` is hand-authored
against `@radix-ui/react-select` (a new dependency; installed via plain `npm install`, which has no
such prompt) matching the styling conventions already established in `dialog.tsx`/`sheet.tsx`.

`GeographyPicker` (`src/web/src/features/branches/GeographyPicker.tsx`): three cascading `Select`s
— Province, District, Local Level — each populated by its own BFF-proxied geography endpoint,
filtered by the parent's selected id, and disabled until that parent has a value. A District/Local
Level `Select` whose parent changes clears its own (now-invalid) selection. A plain number `Input`
for Ward (the backend stores `Branch.WardNumber` as a bare int scoped to `LocalLevelId`, not a
`Ward`-entity foreign key — confirmed by reading the `Branch` domain model — so no fourth cascading
select or Ward-list fetch is needed). Local Level options display as `"{Name} ({Type})"` (e.g.
"Kathmandu (Metropolitan)") since `LocalLevelType` (`Metropolitan`/`SubMetropolitan`/
`Municipality`/`RuralMunicipality`) disambiguates same-named local levels in different districts —
CLAUDE.md §8 lists this as real routing/pricing-relevant data, not decoration.

Four new BFF routes, one per geography level, each a thin proxy accepting the parent id as a query
param exactly like the backend does (`GET /api/geography/provinces`, `.../districts?provinceId=`,
`.../local-levels?districtId=`, `.../wards?localLevelId=` — the last one is *not* used by
`GeographyPicker` itself, since branches use a plain ward number, but is included now since the
same four-level cascade will be needed again by later phases' address-entry forms, e.g. merchant
pickup addresses).

## 6. Branch detail (record + delivery zone)

Two sections on one page (no tabs — matches the single-page-of-stacked-`Card`s pattern F1
established for the organization dashboard, avoiding a UI pattern this app hasn't used anywhere
else yet):

1. **Branch record** — every `BranchResponse` field rendered read-only (Code, Name, Type, resolved
   geography chain as text, Address, Phone, Operating Hours, Parent Branch as a link to its own
   detail page if present, Active status). A **Delivery Zone** row is the one editable field: a
   `Select` (populated from `GET /api/v1/delivery-zones`, already permission-gated `zone.read`
   server-side) that calls the existing `PUT /api/v1/branches/{id}/delivery-zone` BFF-proxied route
   on change, gated by `usePermission("branch.update", { branchId: id })` — F0's branch-scoped
   permission check, exercised for the first time by any phase.
2. **Branch dashboard** — see §7.

## 7. Branch dashboard content

Same shape as Phase F1's organization dashboard: a BFF proxy route
(`GET /api/reports/dashboard/branch/[id]`), a `useBranchDashboard(branchId)` hook via `clientFetch`,
`Card`-based sections matching the established icon-badge-plus-subtitle language. Mapped from
`BranchDashboardResponse`:

- **Shipments** — Outgoing, Pending Sorting, Pending Dispatch (`ShipmentCounts`).
- **Manifests** — In Transit Toward This Branch, Recently Closed (`ManifestCounts`).
- **Today's Deliveries** — Attempts, Successes, and a computed success rate
  (`Successes / Attempts`, `—` when `Attempts` is `0`, matching F1's `formatPercent` null-handling
  convention) from `TodaysDeliveries`.
- **Exceptions** — Missing Shipment Exceptions, Open NDR Count, and the riders aggregate
  (`riders.length` branches with `sum(openTaskCount)` open tasks, per §1's resolution) as three
  tiles in one `Card`, each using the relevant existing status token for its icon accent
  (`status-ndr` for open NDRs, `status-rto`-adjacent brick is *not* reused here since exceptions
  aren't RTO-specific — missing-shipment and rider-load tiles use neutral `muted` icon badges,
  reserving color for genuinely urgent figures per the same restraint `StatusBadge` already
  practices).
- **COD Pending Finance Verification** — one currency tile, same `Intl.NumberFormat` compact
  formatting F1 established for `FinancialSummary`.

## 8. What this spec deliberately does not decide

- The exact `Select`/form visual treatment beyond matching established conventions — implementation
  work product, same division of labor as every prior phase's creative tasks.
- A map-based lat/long picker for branch creation — not built this phase (§4).
- Branch editing beyond delivery zone — blocked on a backend endpoint that doesn't exist
  (`CLARIFICATIONS.md`, this phase).
- Named per-rider dashboard breakdowns — Phase F8, per F1's own precedent, reaffirmed here (§1).
- Whether `GeographyPicker` becomes a shared component other future forms import directly, or each
  phase copies the pattern — decided when Phase F4 (merchant addresses) or a later phase actually
  needs it again; premature to force reuse now for a single caller.
