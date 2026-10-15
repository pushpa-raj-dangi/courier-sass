# F15 Console Redesign — R3b-2: People & Places (Riders, Live map, Fleet, Branches) — Design Spec

## 1. Context

R3b-2 is the second half of the old R3b row ("Riders + vehicles, live map, branches"). R3b-1
(parcel movement) merged on 2026-10-15 at `373d808`.

Design source: the "Network: people and places" row of the canvas
https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu (Version 15). The user approved these artboards on
2026-10-15:
- `Riders.dc.html`: riders board and rider Sheet.
- `RidersMap.dc.html`: Map tab.
- `Fleet.dc.html`: Fleet tab and Add vehicle Sheet.
- `Branches.dc.html`: branches page and branch Sheet.

Decisions made while brainstorming (user-approved):
1. **Backend scope.** A small read slice, the same rules as R3b-1: no migrations, no new entities, no
   new writes. It adds two computed reads (a rider board and branch activity) and one display-name
   lookup.
2. **Live map.** It becomes a **Map tab on `/riders`**, sharing the page's branch filter. The old
   `/riders/live-map` route redirects to `/riders?tab=map`, and its nav item is removed.
3. **Vehicles.** They become a **Fleet tab on `/riders`**. The rider Sheet also shows the rider's
   vehicle.
4. **Read shape.** One batched read per page:
   - `GET /api/v1/riders/board` gives one row per rider for today.
   - `GET /api/v1/branches/activity` gives one row per branch with live custody counts.
   - Cash held is not duplicated. The web joins the existing R3a `GET /api/v1/cod/rider-cash`.

### Scope boundary (same as R2, R3a and R3b-1)
- Frontend restyle plus the read slice in §3. There are no writes beyond the existing ones: create
  rider, create vehicle, assign task, create branch, change delivery zone.
- Data the API does not serve renders `ComingSoonSlot` or a disabled control with a "Needs backend"
  tooltip. Values are never mocked.
- Detail views are right-side Sheets driven by Zustand panel stores, never routed pages.
- shadcn/ui only. Type uses on-scale classes only. Status is never conveyed by colour alone.
- Loading and error states never show `0`. Use skeletons, `ErrorState` or "—".

### Code facts this spec follows (code wins over the artboards)
- **Runs.** `DeliveryRunStatus` is `Active | Completed` only, so the strip has three stages: Off duty
  (no run today), On run (Active) and Run done (Completed). Any "planned" stage in earlier
  discussion is dropped.
- **Today.** "Today" is `DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date)`, matching
  `GetMyTasksTodayAsync` and the location ping code. For Nepal that is wrong between 00:00 and 05:45
  local time. This is a known follow-up (§9), not changed here.
- **Rider names.** These live on Identity `User`. Today the web resolves them through
  `usePeopleNames()`, which needs `user.read`, and most operations roles lack it. The board
  therefore returns `fullName` server-side through a new read-only Identity lookup (§3.1).
- **Riders at a branch.** "Riders at a branch" means `Rider.BaseBranchId`. This is the existing
  convention in `GetBranchLocationsAsync` and `GetBranchRiderTaskLoadAsync`.
- **Open exceptions.** On the branch page these are **open NDRs** (status ≠ Resolved) whose
  shipment's `DestinationBranchId` is the branch. This matches `GetOpenNdrCountForBranchAsync`, the
  branch dashboard's `OpenNdrCount` and the NDR list's branch scope, so the number agrees with the
  NDR queue it links to.
- **Branch Sheet data.** The branch Sheet keeps `GET /reports/dashboard/branch/{id}` (`report.read`)
  for its existing Shipments and Exceptions sections.
- **Vehicle type.** `Vehicle.Type` is a free string. The Add vehicle form keeps its current input
  behaviour (§5.4).

## 2. Artboard elements vs data source

| Artboard element | Source |
|---|---|
| Riders strip: Off duty / On run / Run done counts | client count over `riders/board` rows |
| Strip highlight "‹N› with stale location" | rows On run with `lastPingAt` null or older than 15 min |
| Rider name, role, plate | board `fullName`, `roleType`, `vehiclePlate` |
| Today "‹done› of ‹assigned›", failed | board `tasksCompleted`, `tasksAssigned`, `tasksFailed` |
| Last seen | board `lastPingAt` (relative; "stale" text past 15 min) |
| Cash held | `GET /cod/rider-cash?branchId` `amount` by `riderId` (needs `cod.read`; else the column is hidden) |
| Rider Sheet: today's task list | new `GET /riders/{id}/today` (§3.1) |
| Rider Sheet: route mini-map | existing `GET /riders/runs/{runId}/breadcrumbs` |
| Rider Sheet: cash card | `rider-cash` item: `amount`, `parcelCount`, `oldestCollectedAt` |
| Rider Sheet: attendance and history | ComingSoonSlot |
| Map pins | existing `GET /riders/branches/{branchId}/locations` and board names |
| "On run, not on the map" list | board rows On run whose rider has no location row |
| Fleet table | existing `GET /vehicles` and board rows (rider name and today) |
| Fleet "Unassigned" chip | client filter `riderId == null` |
| Reassign or retire vehicle | Needs backend note |
| Branches subtitle and type strip | existing `GET /branches/summary` (`active`, `inactive`, `byType`) |
| Branch table district | existing geography names lookup used by `BranchRecord` |
| On hand / Out for delivery / Riders out / Exceptions | new `GET /branches/activity` |
| Branch Sheet facts | existing `GET /branches/{id}` |
| Branch Sheet "right now" cards | `branches/activity` row for that branch |
| Branch Sheet map | branch lat/long and `riders/branches/{id}/locations` (`rider.location.read`) |
| Branch Sheet exceptions list | existing `BranchExceptions` (branch dashboard) |
| Edit details, roster, weekly hours | ComingSoonSlot |

## 3. Backend slice (read-only)

There are no migrations, no new entities, no new writes, and no change to existing write behaviour.
Tenant isolation stays on the global query filters. Branch scope comes from
`IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, permission)`, where null means all
branches. A `branchId` filter only narrows within that scope. Every new read is batched: a fixed
number of queries per request, never one per row.

### 3.1 Riders module

**Identity lookup (new, read-only).** Add `IUserDirectoryLookupService` in
`src/Modules/Identity/Application`:

```csharp
Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
```

- It is implemented on the tenant-filtered `User` set and registered in DI next to
  `IUserProvisioningService`.
- An empty input means no query.
- Ids from another tenant are absent from the result, because the global filter excludes them.

**`GET /api/v1/riders/board?branchId=`** requires `rider.read`. Service:
`RiderService.Board.cs` (a new partial), `GetBoardAsync(Guid? branchId, ClaimsPrincipal user, ct)`.

```csharp
public record RiderBoardRunSummary(Guid Id, DeliveryRunStatus Status);
public record RiderBoardRow(
    Guid RiderId, Guid UserId, string FullName, Guid BaseBranchId, RiderRoleType RoleType, bool IsActive,
    string? VehiclePlate, RiderBoardRunSummary? Run,
    int TasksAssigned, int TasksCompleted, int TasksFailed, DateTimeOffset? LastPingAt);
public record RiderBoardResponse(IReadOnlyList<RiderBoardRow> Items);
```

- **Scope.** Riders whose `BaseBranchId` is in `GetAccessibleBranchIdsAsync(user, "rider.read")`,
  then narrowed by `branchId`. A `branchId` outside scope gives an empty `Items`. Inactive riders are
  included with `IsActive = false`, and the web filters them.
- **Queries (fixed count):**
  1. Riders in scope.
  2. Today's runs for those riders: `Date == today`. If a rider has more than one, take the
     `Active` one first, else the latest by `Id`. That is deterministic, and the domain should allow
     only one.
  3. Task counts grouped by `DeliveryRunId` and `Status` for those run ids. Assigned is all tasks,
     Completed is `Completed`, Failed is `Failed`.
  4. Latest ping per rider: `GroupBy(RiderId).Select(max RecordedAt)` over pings for those riders.
  5. Vehicles with `RiderId` in the set. If a rider has several, take the first by `PlateNumber`.
  6. `GetFullNamesAsync(userIds)`. A missing name becomes `"Rider"`.
- **Ordering.** On run first, then Run done, then Off duty, then by `FullName`.

**`GET /api/v1/riders/{id}/today`** requires `rider.read`. It is the rider Sheet's task list.

```csharp
public record RiderTodayTask(Guid TaskId, Guid ShipmentId, string Awb, DeliveryTaskType Type, int Sequence, DeliveryTaskStatus Status);
public record RiderTodayResponse(RiderBoardRunSummary? Run, IReadOnlyList<RiderTodayTask> Tasks);
```

- An unknown rider, or a rider whose `BaseBranchId` is out of `rider.read` scope, returns
  **404**, never 403.
- AWBs come from one batched `IShipmentReportingLookupService.GetAwbsByShipmentIdsAsync`. This is
  the same call R3b-1 Task 1 uses. `RiderService` does not depend on that interface today: it has
  only `IShipmentTaskLookupService`, which is single-shipment. It gains the dependency as a new
  constructor parameter, and every test call site is updated. A missing AWB shows as `""`.
- Ordered by `Sequence`.

**Security fix: existing `GET /api/v1/riders/{id}`.** `RiderService.GetByIdAsync` today returns any
rider in the tenant, with no branch scope. That is the same gap R3b-1 fixed for manifests. It is
changed to take the acting user and return null, which becomes **404**, when the rider's
`BaseBranchId` is outside `GetAccessibleBranchIdsAsync(user, "rider.read")`. Its callers are
updated. Unit and API tests pin both 404 cases (unknown, and out of scope) and the 200 case for an
in-scope user. The Sheet header uses this read.

### 3.2 Branch activity (Reports module)

**`GET /api/v1/branches/activity`** requires `branch.read`. It is mapped in the Reports module next
to the branch dashboard, because Reports already composes the cross-module lookups. Service:
`BranchActivityService.GetAsync(ClaimsPrincipal user, ct)`.

```csharp
public record BranchActivityRow(Guid BranchId, int OnHand, int OutForDelivery, int RidersOut, int OpenExceptions);
public record BranchActivityResponse(IReadOnlyList<BranchActivityRow> Items);
```

- **Scope.** Branches from `GetAccessibleBranchIdsAsync(user, "branch.read")`. When that is null,
  use all active and inactive branches of the tenant through `IBranchLookupService`. There is one row
  per branch in scope, including all-zero rows.
- **New batched lookup methods.** All take `IReadOnlySet<Guid>? branchIds`, where null means no
  filter, and return `IReadOnlyDictionary<Guid, int>` or a tuple dictionary. Each is one grouped
  query:
  - `IShipmentReportingLookupService.GetBranchCustodyCountsAsync`, grouping by `CurrentBranchId`:
    - **OnHand.** Status is one of `AtOriginBranch`, `Sorted`, `AtDestinationBranch`,
      `DeliveryFailed`, `NdrCreated`, `RtoAtOrigin`.
    - **OutForDelivery.** Status is `OutForDelivery`.
  - `IRiderReportingLookupService.GetRidersOutCountsAsync(branchIds, today)`. This counts distinct
    riders, grouped by `BaseBranchId`, with a run where `Date == today` and `Status == Active`.
  - `INdrReportingLookupService.GetOpenNdrCountsByBranchAsync(branchIds)`. This uses the same join
    as `GetOpenNdrCountForBranchAsync`, grouped by `DestinationBranchId`.

### 3.3 Errors and validation
- An invalid `branchId` format becomes a 400 from model binding, as on other Guid query
  parameters.
- Permission failures give 403. An unauthenticated caller gets 401. Out-of-scope single reads
  return 404.

## 4. Shared web pieces
- **Hooks:**
  - `useRiderBoard(branchId?)`, key `["riders", "board", branchId ?? null]`.
  - `useRiderToday(riderId, enabled)`, key `["riders", riderId, "today"]`.
  - The existing `features/cod/hooks/useRiderCash`, used as is.
  - `useBranchActivity()`, key `["branches", "activity"]`.
  - Existing `useRiderLocations` and `useVehicles`.
- **Proxy routes.** `app/api/riders/board`, `app/api/riders/[id]/today` and
  `app/api/branches/activity`, built the same way as the existing proxies.
- **`features/riders/lib/riderModel.ts`** (pure):
  - `riderDayStage(row) → "off" | "on" | "done"`
  - `RIDER_STAGES`
  - `STALE_LOCATION_MINUTES = 15`
  - `isLocationStale(lastPingAt, now)`
  - `riderStageCounts(rows)`
  - `parseRidersTab` (`riders | map | fleet`, default `riders`)
  - `parseRiderStage`
- **`features/branches/lib/branchModel.ts`** (pure):
  - `BRANCH_TYPES` order and labels
  - `parseBranchType`
  - `matchesBranchQuery(branch, q)`, a case-insensitive match on code or name
- **Panel store.** `riderPanelStore` gains `{ mode: "detail"; riderId: string }` and
  `openDetail(riderId)`.
- Reuse R3b-1's `PipelineStrip` (select and highlight), `useSearchParamState`, `useFilterPage`,
  `toClientPage`, `formatAge` and `branchCode`.

## 5. Riders page (`app/(staff)/riders/page.tsx`)

### 5.1 Shell
- **URL state:** `?tab=riders|map|fleet` (`riders` is the default and is not written),
  `?stage=off|on|done`, `?branch=<id>`, `?role=Pickup|Delivery`, `?inactive=1` (show inactive). One
  merged patch per action.
- **Header:** "Riders" and the subtitle "‹branch name or All branches› · ‹N› out today", where N is
  the On run count; "—" while loading.
- **Branch picker.** Shown when the user has all-branches scope or more than one branch.
- **Actions.** Assign task (`delivery.assign`), Add rider (`rider.create`) and Add vehicle
  (`rider.create`, Fleet tab only). All use the existing Sheets.
- **Strip.** `PipelineStrip` over the three stages. Selecting a stage filters the Riders tab. The
  On run stage is highlighted when any On run rider is stale, with the sub-text "‹N› with stale
  location". It shows a skeleton while loading and `ErrorState` on error.
- **Tabs.** Riders (count = shown rows), Map (hidden without `rider.location.read`), Fleet
  (count = vehicles).

### 5.2 Riders tab
- **Columns:**
  - Rider: initials avatar, `fullName`, role.
  - Vehicle: plate, or "Unassigned".
  - Today: "‹done› of ‹assigned›" with a bar and "‹failed› failed", or "No run".
  - Last seen: relative time, or "No ping today". Stale shows the text "‹age› · stale".
  - Cash held: Rs. Shown only with `cod.read`. "—" when the rider has no holding row.
  - Status: a badge with label and dot.
- **Paging.** Client-paged at 20 with `toClientPage`. The page resets on any filter change.
- **Row click.** Opens the rider Sheet.

### 5.3 Map tab
- **Component.** The existing `LiveMap` (dynamic import, `ssr:false`), restyled:
  - Pins are `L.divIcon` initials badges.
  - A fresh ping is a solid teal disc. A stale ping is dashed, with an "‹N› min" label.
  - Clicking a pin opens a popover: name, role and plate, "‹done› of ‹assigned›", last ping,
    accuracy, cash if `cod.read`, and an "Open rider" link that calls `openDetail`.
- **Branch pin.** Shown when the branch has coordinates.
- **Refresh.** Polls every 20 s, the existing interval. The legend text shows the last updated
  time.
- **No branch picked** (all-branches user): an `EmptyState` saying "Pick a branch to see its
  riders on the map". This matches the existing page behaviour.
- **Riders without a location.** On run riders with no location row are listed in the "On run, not
  on the map" box, never placed on the map.
- **Leaflet tiles and icons.** These stay as today (OpenStreetMap tiles and the CDN icon fix). No
  new dependency.

### 5.4 Fleet tab
- **Table.** Plate, Type, Capacity ("‹n› kg" or "—"), Assigned rider (name or "Unassigned"), Rider
  today (stage and "‹done› of ‹assigned›" from the board).
- **Chips.** All ‹n› and Unassigned ‹n›, as client filters held in `?fleet=unassigned`.
- **Add vehicle.** The existing `VehicleCreateSheet`, restyled. Its fields, validation and request
  are unchanged.
- **Note line.** "Reassigning a vehicle or marking it off the road needs backend support."

### 5.5 Rider Sheet (new `RiderDetailSheet`)
- **Header.** Avatar initials, eyebrow "RIDER · ‹ROLE› · ‹branch code›", name, a stage badge, and
  facts for base branch, licence, vehicle and account (Active or Inactive).
- **Today card.** "‹done› of ‹assigned› done · ‹failed› failed", and a segmented bar for completed
  and failed. The task list from `riders/{id}/today` shows sequence, AWB (clicking it opens the
  shipment Sheet), type and status text. It shows the first 8 and "+ ‹n› more tasks". With no run,
  it reads "No run today".
- **Location card.** Needs `rider.location.read`; otherwise it is hidden.
  - It shows a mini Leaflet map of today's breadcrumbs, with the start point and latest point
    marked.
  - It shows "Last ping ‹age› · ±‹m› m · ‹n› pings today".
  - With no run or no pings, it reads "No location today".
- **Cash card.** Needs `cod.read`; otherwise it is hidden.
  - It shows the amount, "From ‹n› COD parcels · oldest collected ‹age› ago", and an "Open in COD"
    link to `/cod?branch=`.
  - With no holding, it reads "No cash held".
- **ComingSoonSlot.** "Attendance and delivery history".
- **Footer.** "Counts are today's run only (date in UTC)", and Assign task (`delivery.assign`).

## 6. Branches page (`app/(staff)/branches/page.tsx`)

### 6.1 Shell
- **URL state.** `?type=<BranchType>`, `?q=` and `?inactive=1`. The default is active only.
- **Header.** "Branches", with the subtitle "‹active› active · ‹inactive› inactive" from the
  summary. Add branch (`branch.create`) uses the existing Sheet.
- **Type strip.** A five-cell selectable strip with counts from `summary.byType` and no highlight.
  It shows a skeleton while loading and `ErrorState` on error.
- **Filters.** A search input (code or name) and an "Active only" checkbox.

### 6.2 Table
- **Columns:**
  - Branch: name, with the code in mono.
  - Type: a badge.
  - District.
  - On hand.
  - Out for delivery.
  - Riders out.
  - Exceptions: "None", or "‹n› open" in warning text with its own words.
- **Activity loading and errors.** Activity cells show "—" while activity is loading or errored.
  The table still renders from the branch list, and an inline `ErrorState` above the table offers
  a retry.
- **Paging.** Client-paged at 20.
- **Row click.** Opens the branch Sheet.

### 6.3 Branch Sheet (existing `BranchDetailSheet`, restyled)
- **Header.** Eyebrow "BRANCH · ‹TYPE› · PARENT ‹code›", name, mono code and an active badge.
- **Facts.** Address (ward, local level, district), phone and hours, delivery zone, and
  coordinates. A missing value shows "—".
- **"Right now" cards.** The four activity numbers, each a link:
  - On hand goes to `/shipments?branchId=<id>`, plus one `status` per on-hand status.
  - Out for delivery goes to `/shipments?branchId=<id>&status=OutForDelivery`.
  - Riders out goes to `/riders?branch=<id>&stage=on`.
  - Exceptions goes to `/ndrs`.

  The shipments page's `branchId` filter matches origin, destination **or** current branch, so the
  linked list can be longer than the custody count. The link text therefore reads "View shipments",
  never "View ‹n›".
- **Location card.** Shown when the branch has coordinates and the user has `rider.location.read`:
  a mini map with the branch marker and riders' latest pings. Otherwise it shows the address only.
- **Kept sections.** The existing Shipments and Exceptions sections stay, restyled, with no data
  change.
- **ComingSoonSlot.** "Edit details, staff roster, weekly hours".
- **Footer.** Change delivery zone, using the existing action and permission.

## 7. Navigation
- **Remove** the `liveMap` nav item.
- **Redirect.** `app/(staff)/riders/live-map/page.tsx` redirects to `/riders?tab=map`, preserving
  `branch` when present.
- **Command palette.** Update any entries that pointed to `/riders/live-map`.

## 8. Tests
- **Backend unit tests (InMemory):**
  - **Board:**
    - scope narrowing
    - a `branchId` out of scope gives empty results
    - no run means stage Off and zeros
    - Active and Completed runs
    - grouped task counts
    - latest ping chosen
    - no pings gives null
    - vehicle plate chosen
    - name fallback
    - inactive rider included
    - another tenant excluded
  - **Today:** 404 out of scope, 404 unknown, ordering, AWB batch called once, no run gives empty.
  - **Branch activity:**
    - one row per branch in scope, including zeros
    - custody status buckets
    - riders out counts only today's Active runs
    - open NDRs by destination
    - all-branches scope
  - **Identity lookup:** an empty input runs no query, and another tenant's users are excluded.
- **API tests (Postgres):** for each new endpoint, 200, 403 without permission, 401, and a
  branch-scoped user seeing only their own branch. For `GET /riders/{id}`: 404 out of scope, 404
  unknown, 200 in scope.
- **Web tests:**
  - `riderModel` and `branchModel` unit tests.
  - Hook query keys.
  - Riders page:
    - tabs and URL
    - strip filter
    - stale highlight
    - cash column hidden without `cod.read`
    - Map tab hidden without `rider.location.read`
    - map empty state without a branch
    - "not on the map" list
  - Fleet tab: the Unassigned filter.
  - Rider Sheet: loading, error, no run, cards hidden by permission.
  - Branches page: type strip filter, search, active toggle, activity "—" while loading.
  - Branch Sheet: activity links, location card gating.
  - The live-map redirect.
- **Full verification:** `dotnet build`, the listed test projects, web `tsc --noEmit` with 0
  errors, lint, `npm test` and `npm run build`. Then a live check in Chrome; the user signs in.

## 9. Exit criteria and follow-ups

### Exit criteria
- The four artboards are implemented to the data rules in §2. The new reads are branch-scoped,
  return 404 not 403 on single reads, and are tested. `GET /riders/{id}` is branch-scoped
  (security fix).
- `/riders/live-map` redirects to the Map tab, and its nav item is gone.
- All checks in §8 pass, and the docs are updated: API-SPEC (new reads), FRONTEND-ROADMAP (R3b-2
  row and follow-ups), and DESIGN.md if a new shared pattern appears.

### Non-goals
- Rider, vehicle or branch edits.
- Assigning a vehicle to a rider after creation.
- Attendance and history.
- Real-time push. Polling stays.
- Route optimisation (backend Phase 14).

### Backend follow-ups (log in FRONTEND-ROADMAP F15)
1. Organisation-local "today" (Nepal time) for runs, pings and the board.
2. Reassign or retire a vehicle; edit rider details; activate or deactivate a rider.
3. Edit branch details, staff roster and weekly operating hours.
4. Rider attendance and 30-day delivery history.
5. Rider phone on the rider read, for click-to-call from the Sheet.
6. Batch the per-rider latest-ping loop in `GetBranchLocationsAsync`.
