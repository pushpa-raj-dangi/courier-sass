# F15 Console Redesign — R2: Staff Operations — Design Spec

## 1. Context

R2 is the second sub-phase of Frontend Phase F15 (see
`docs/superpowers/specs/2026-09-30-f15-r1-console-foundation-design.md` and
`docs/FRONTEND-ROADMAP.md` Phase F15). R1 delivered the shared `ConsoleShell`, tokens and page
primitives (`PageHeader`, `SectionCard`, `KpiTile`, `PipelineStrip`, `ComingSoonSlot`,
`SheetHeaderBlock`). R2 redesigns three staff surfaces against the design canvas
(claude.ai artifact https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu, artboards `Main`,
`Shipments`, `Scan`):

1. **Dashboard** → "Operations overview".
2. **Shipments** list + detail Sheet.
3. **`/scan` Scan station** (new route).

### Deviation from the F15 standing rule (approved)

F15's standing rule is "frontend only". R2 carries a **small, read-only backend slice** in the
Shipments module (§3), approved during brainstorming, because without it the Shipments tabs
cannot filter and four of six Sheet tabs would be placeholders. It also closes a branch-scope
gap found during analysis (§3.3). Everything else the design shows but the API does not serve
still renders a `ComingSoonSlot` — never mock numbers (DESIGN.md No-Fake-Numbers Rule).

## 2. Goals / Exit Criteria

- Dashboard, Shipments list, Shipment Sheet and `/scan` match the canvas layouts using R1
  primitives, in light and dark themes, at 375 px with no horizontal page scroll.
- Shipments tabs, search and filters work server-side; filter state lives in the URL.
- Sheet shows "Right now" custody, a status stepper, Timeline and Custody chain from real
  `ShipmentEvent` data.
- Scan station supports camera-first continuous scanning plus manual/keyboard-wedge entry for
  Receive, Dispatch, Rider handover and RTO, using existing write endpoints only.
- `GET /api/v1/shipments/{awb}` enforces staff branch scope.
- `dotnet build`, `dotnet test`, `npm run build`, `npm run lint`, `npm test` pass;
  `tsc --noEmit` shows no errors beyond the 8 pre-existing ones on master.
- Docs updated (§9).

### Non-goals

- Any write endpoint, migration, or new domain entity.
- Standalone Sort operation, sort bins, label printing, rider reassignment, bulk CSV upload,
  row bulk-selection actions (no backing operations — follow-ups, §10).
- Server-side actor display names.
- Pages outside the three surfaces (R3–R6).
- New npm dependencies (`@zxing/browser` already present).

## 3. Backend slice (Shipments module, read-only)

### 3.1 `GET /api/v1/shipments` filters

New optional query parameters, AND-combined, applied **after** the existing tenant +
branch-scope (staff) / merchant pin (merchant) predicates, so they can only narrow results:

| Param | Type | Semantics |
|---|---|---|
| `status` | repeatable `ShipmentStatus` | `IN` match on `CurrentStatus`. Unknown value → 400 (standard error contract, code `VALIDATION_ERROR`). |
| `q` | string | Trimmed. Length < 2 → 400. Matches AWB prefix (case-insensitive, `ILIKE 'q%'`) **or** recipient name contains (case-insensitive) **or** recipient phone contains. `%`/`_` in input are escaped. |
| `merchantId` | Guid | Equality. Ignored-by-construction for merchant callers (their pin already applies; a different id yields an empty page). |
| `serviceTypeId` | Guid | Equality. |
| `branchId` | Guid | Origin **or** destination **or** current branch equals it. If outside the caller's accessible set, the branch-scope predicate already excludes everything → empty page (no 403, no leak). |
| `createdFrom`, `createdTo` | `DateTimeOffset` | Inclusive range on `CreatedAt`. `createdFrom > createdTo` → 400. |

Ordering and pagination unchanged (`CreatedAt desc, Id`).

**Response change:** `ShipmentResponse` gains `DateTimeOffset? LastEventAt` (max
`ShipmentEvent.Timestamp` for the shipment). For the list it is fetched with **one** grouped
query over the page's shipment ids; the per-row items query (current N+1) is replaced by **one**
batched query grouped in memory. `GET /{awb}` populates it too. The field is additive; merchant
API consumers are unaffected beyond the new property.

### 3.2 `GET /api/v1/shipments/{awb}/events` (new)

- Staff only: requires `shipment.read`; a merchant caller → 403.
- Shipment resolved by AWB under tenant isolation and the §3.3 branch-scope rule; not found or
  out of scope → 404.
- Response: array ordered by `Timestamp` ascending, then `Id`:

```json
[{ "id": "…", "eventType": "ManifestReceived", "previousStatus": "InTransit",
   "newStatus": "AtDestinationBranch", "branchId": "…", "userId": "…", "riderId": null,
   "timestamp": "2026-10-05T08:15:00Z", "notes": "…" }]
```

- **Not exposed:** `Metadata`, `Latitude`, `Longitude`.
- Read-only; not audited (consistent with other reads).

### 3.3 Branch scope on single-shipment reads (security fix)

Today `ShipmentService.GetByAwbAsync` checks tenant and merchant ownership but not staff branch
scope. New rule, identical to the list predicate: a staff caller may read a shipment only if
`GetAccessibleBranchIdsAsync(user, "shipment.read")` is `null` (all branches) **or** contains
the shipment's origin, destination, or current branch. Otherwise the endpoint returns **404**
(not 403 — AWB existence is not disclosed). Applied to `GET /{awb}` and `GET /{awb}/events`.

Internal callers (`GetScanInfoByAwbAsync`, transition services) are **not** changed — scan and
transition endpoints already perform their own branch-access checks. Recorded as
CLARIFICATIONS #25.

## 4. Dashboard ("Operations overview", `app/(staff)/dashboard/page.tsx`)

Each block fetches/handles its own loading and error state; one failing endpoint never blanks
the page.

| Block | Source | Behaviour |
|---|---|---|
| Header | — | `PageHeader` title "Operations overview", description "N branches in scope" (from `useBranches`). Actions: range segmented control **Today · 7 days · 30 days · Custom** (Custom reveals the existing two `DatePicker`s; ranges sent UTC-anchored as today), **Export** (Popover containing the existing `ExportReportsCard` content), **Book shipment** (`shipment.create` → `openCreate`). |
| Parcel pipeline | `/shipments/summary` | `PipelineStrip`, current WIP: Booked = Created+PickupAssigned · Picked up = PickedUp · At origin = AtOriginBranch+Sorted · In transit = InTransit · At destination = AtDestinationBranch · Out for delivery = OutForDelivery. Each `href` → `/shipments?tab=<tab>` (§5.1). 7th stage **Delivered** = Σ `branchPerformance[*].deliverySuccesses` for the range, caption "in range", no link. No prior-period captions. |
| KPI row | org dashboard | 4× `KpiTile`: Delivery success rate, NDR rate, RTO rate, Avg booking → delivery (TimeSpan formatted as days/hours). `note` = range label. **No `delta`.** Null values render "—". |
| Hourly flow | — | `ComingSoonSlot` ("Hourly flow — booked vs delivered by hour"). |
| Needs action | `/ndrs/summary`, `/return-shipments/summary`, `/manifests/summary` | Rows: Open NDRs (sum of non-`Resolved` statuses; `ndr.read`) → `/ndrs`; RTOs awaiting merchant sign-off (`pendingConfirmation`; `rto.read`) → `/return-shipments`; Manifests in transit (`InTransit` count; `manifest.read`) → `/manifests`. A row whose permission is missing is hidden; zero renders muted. Footer: small `ComingSoonSlot` ("Missing parcels, riders holding cash, SLA risk"). |
| COD (range) | org dashboard `financial` | Headline Expected. 3-segment bar: Settled · Collected not settled (`collected − settled`, floored at 0) · Not yet collected (`expected − collected`, floored at 0), each with label + value (NPR). Merchant payable callout with **Review** → `/cod` (`cod.read`). Small `ComingSoonSlot` for rider-held vs deposited split. |
| Branch performance | org dashboard `branchPerformance` + `useBranches` | Table: Branch (name + code) · Volume · Delivered · Success rate (inline bar + %) · NDR · RTO. Sorted by volume desc. Unknown branch id → "Unknown branch". |
| Rider performance | org dashboard `riderPerformance` + `useRiders` (only with `rider.read`) | Compact top-5 by success count: Rider · Attempts · Delivered · Success rate. Without `rider.read` the rider column shows "Rider" + short id. |

Layout: pipeline → KPI row (4 → 2 → 1 cols) → `[Hourly flow 2fr | Needs action 1fr]` →
`[COD 1fr | Branch performance 2fr]` → Rider performance; single column below `lg`; tables
scroll inside their card.

**Removed** (unused after migration, with their tests): `QuickStatsStrip`, `ShipmentFunnel`,
`DeliveryRateHero`, `RateTiles`, `BranchPerformanceChart`, `RiderPerformanceChart`
(`features/reports/components/`). `DashboardHeader` is removed if nothing else imports it.
`ExportReportsCard` stays (re-hosted in the popover).

## 5. Shipments

### 5.1 List page (`app/(staff)/shipments/page.tsx`)

- `PageHeader` "Shipments", description "{totalCount} shipments in your branch scope" (from the
  current query's `totalCount`). Action: **Book shipment** (`shipment.create`).
- **Tabs** (shadcn `Tabs`), counts from `/shipments/summary`; status groups shared with the
  dashboard pipeline in one module (`features/shipments/lib/statusGroups.ts`):

| Tab (`?tab=`) | Statuses |
|---|---|
| `all` | none (no `status` param) |
| `at-branch` | Created, PickupAssigned, PickedUp, AtOriginBranch, Sorted, AtDestinationBranch |
| `in-transit` | InTransit |
| `out-for-delivery` | OutForDelivery |
| `ndr` | DeliveryFailed, NdrCreated |
| `rto` | RtoInitiated, RtoInTransit, RtoAtOrigin, RtoDelivered |

  `Delivered`, `Cancelled` and the eight international statuses (`CustomsPrepared` …
  `ReceivedFromCarrier`) belong to no tab and appear under **All** only (the canvas has no tab
  for them).

  The dashboard pipeline links use finer groups via an explicit `status` URL param when a stage
  doesn't equal a tab (e.g. Booked → `?status=Created&status=PickupAssigned`); explicit `status`
  params win over `tab` (no tab highlighted then). The module is the single source both pages
  import.
- **Filter bar**: search input (debounced 300 ms; sent only when ≥ 2 chars) → `q`; chips as
  `Popover` + `Command`: Branch (`useBranches`), Merchant (`useMerchants`), Service
  (`useServiceTypes`), Booked (Today / 7 days / 30 days / Custom range → `createdFrom/To`).
  **Clear filters** when any is set. All state in URL search params (`useSearchParams` +
  `router.replace`); query key includes every param; any filter change resets `page` to 1.
- **Table** (`DataTable`): AWB (mono, booked date/time below) · Merchant · Recipient (name,
  address line below) · Route (`ORIGINCODE → DESTCODE`) · Service · COD (right-aligned; "Prepaid"
  when 0) · Status (`StatusBadge`) · Last scan (`lastEventAt` relative, "—" if null). Row click /
  AWB button → `openDetail(awb)`; the open row passes `selectedRowId`.
- Empty: no filters → existing "No shipments yet"; with filters → "No shipments match these
  filters" + Clear filters. Error → `ErrorState` with retry.

### 5.2 Detail Sheet (`features/shipments/components/ShipmentDetailSheet.tsx`)

Still driven by `shipmentPanelStore` (keyed by AWB); width `sm:max-w-[580px]`; split into
focused components under `features/shipments/components/detail/`.

- **Header** (`SheetHeaderBlock`): eyebrow `AWB · {SERVICE NAME} · COD|PREPAID`; mono AWB
  title; `StatusBadge`; facts Merchant · COD · Weight · Charge (`totalCharge`).
- **Right now** card (cobalt tint, `section aria-label="Current custody"`): Custodian (rider
  name / staff name / "—"), Owning branch (`currentBranchId` name or "In transit"), In custody
  since (timestamp of the most recent event whose branch/user/rider holder differs from the
  previous one, plus elapsed "2 h 18 m"; "—" if events unavailable), Destination branch.
- **Stepper** (`ShipmentProgressStepper`, pure, from `currentStatus`): Booked · Picked up ·
  Origin hub · In transit · Destination · Out for delivery · Delivered. Complete / current /
  upcoming states; NDR statuses stop at "Out for delivery" with an NDR marker; RTO statuses show
  an RTO marker after the last forward step reached. Text labels always present (not colour
  only); `aria-current="step"` on current.
- `ShipmentNdrRtoLinks` above tabs for NDR/RTO statuses (unchanged behaviour).
- **Tabs**:
  - **Timeline** — `useShipmentEvents(awb)`, newest first: event title (mapped from
    `eventType`, fallback to humanised enum), time (date + time), branch name, actor ("Rider
    {name}" / "{staff name}" / "Staff" / "Rider" / "System"), notes.
  - **Custody chain** — `buildCustodyChain(events)` (pure): consecutive events with the same
    holder (riderId, else userId+branchId) collapse into one segment {holder, branch, from, to,
    duration}; current segment open-ended "now".
  - **Parties** — sender, recipient (names, phones, addresses), items, dimensions, declared
    value, charges breakdown (all fields the current Sheet shows).
  - **COD**, **Attempts**, **Audit** — `ComingSoonSlot` each.
- **Footer** (`bg-surface-sunken`): caption "Status changes only through scans and rider
  actions"; **Receive at origin** button when `currentStatus === "PickedUp"` and `shipment.scan`
  (existing mutation).
- **Names**: branches via `useBranches`; riders via `useRiders` only with `rider.read` (else
  "Rider"); staff via `useAdminUsers`
  only when `usePermission("user.read")` — otherwise "Staff".
- **States**: `DetailSkeleton` while loading; 404 → `ErrorState` "Shipment not found or not in
  your branches"; events error → `ErrorState` (retry) inside Timeline/Custody only.

## 6. Scan station (`app/(staff)/scan/page.tsx`)

### 6.1 Nav

`NAV_ITEMS` gains "Scan station" (`ScanLine` icon, href `/scan`, group `operations`, placed
after Dashboard), visible when the user has any of `shipment.scan`, `manifest.create`, `manifest.receive`,
`delivery.assign`; appears
in the command palette via the single source. en + ne labels.

### 6.2 Modes (registry: `features/scanning/modes.ts`)

```ts
type ScanContext = { kind: "none" } | { kind: "manifest"; manifest: Manifest } | { kind: "rider"; rider: Rider };
interface ScanMode {
  id: "receive" | "sort" | "dispatch" | "handover" | "rto";
  label: string;
  isAvailable: (can: (p: string) => boolean) => boolean;
  context: "none" | "optional-manifest" | "manifest" | "rider";
  manifestFilter?: (m: Manifest, branchIds: string[]) => boolean;
  submit?: (awb: string, ctx: ScanContext) => Promise<ScanOutcome>; // undefined → ComingSoon
}
```

| Mode | Available when | Context | Accepted scan |
|---|---|---|---|
| Receive | `shipment.scan` or `manifest.receive` | optional manifest (offered only with `manifest.receive` + `manifest.read`): status `InTransit`, `toBranchId` in scope; required when the user lacks `shipment.scan` | manifest → `POST /api/manifests/{id}/receive-scan`; none → `GET /api/shipments/{awb}` then `POST /api/shipments/{id}/receive-origin` |
| Sort | whenever the page is visible | — | no `submit`: panel is `ComingSoonSlot` ("Sorting happens when parcels are scanned onto a draft manifest — standalone sort and bins need backend") |
| Dispatch | `manifest.create` + `manifest.read` | manifest: `Draft`, `purpose = Forward` | `POST /api/manifests/{id}/scan` |
| Rider handover | `delivery.assign` + `rider.read` | active rider (`useRiders`, `isActive`) | `GET /api/shipments/{awb}` → `POST /api/riders/tasks { riderId, shipmentId, type: "Delivery" }` |
| RTO | `manifest.create` + `manifest.read` | manifest: `Draft`, `purpose = Return` | `POST /api/manifests/{id}/rto-scan` |

These gates mirror the endpoints' own `RequirePermission` calls (`ManifestsEndpoints`,
`ShipmentsEndpoints`, `RidersEndpoints`); the server stays the authority. **Close manifest**
shows only with `manifest.close`.

`ScanOutcome = { ok: true; awb; summary?: string } | { ok: false; awb; message: string }`.
Errors are normalised through the existing API error shape; manifest errors through
`manifestErrorMessages`; 404 on lookup → "AWB not found in your branches". Mode switcher hides
unavailable modes; default = first available. Mode is kept in `?mode=` for deep links.

### 6.3 Layout

- **Header row**: `PageHeader` (title per mode, e.g. "Receive manifest" / "Receive at origin",
  "Dispatch", "Rider handover", "RTO") with context chips (manifest code, from → to, vehicle /
  rider name); segmented mode switcher (`ToggleGroup` or `Tabs`) on the right, wraps below on
  narrow screens.
- **Context bar**: `Select` for manifest or rider; required context missing → scanning disabled
  with a one-line prompt.
- **Left column**:
  1. **Camera panel** (`components/scanner/BarcodeScanner`, `continuous`): "Start camera" button
     (no permission prompt before tap), live view with aim frame, Stop; denied/unsupported →
     inline message, manual input still works.
  2. **Manual / wedge input**: always present, autofocused after each result, mono, Enter
     submits (uppercased, trimmed), Esc clears.
  3. **Last result** card, `aria-live="polite"`: accepted (teal tint, check icon, large mono
     AWB, summary line) or rejected (destructive tint, message).
  4. **Scan log** (session, newest first): time · AWB · mode · result; count in header.
- **Right column** (stacks below on < `lg`):
  - Receive with manifest → **Manifest reconciliation**: verified / expected
    (`shipmentCount`), proportional bar, "Still expected" list (`useMissingShipments`, refetched
    after each accepted scan; first 5 + "Show all N" expanding in place), **Close manifest**
    (existing `useCloseManifest`, existing confirm dialog copy).
  - Dispatch / RTO → manifest card: code, destination, shipments scanned (`shipmentCount`,
    refreshed after accepted scans), link "Open manifest" → `manifestPanelStore.openDetail`.
  - Rider handover → rider card: name, phone, base branch.
  - Receive / Dispatch → `ComingSoonSlot` "Sort bins".

### 6.4 Scanner component

`features/rider-app/components/BarcodeScanner.tsx` moves to
`src/components/scanner/BarcodeScanner.tsx` (rider page import updated; its tests move with it).
New prop `continuous?: boolean` (default `false` → identical single-shot behaviour) and
`manualEntry?: boolean` (default `true`; the station renders its own manual input and passes
`false`). In continuous mode the stream stays open after a detection; the same code is ignored
for 3 s (`dedupeMs` constant); detection callbacks continue via the existing ref pattern.

### 6.5 Session state

`features/scanning/scanSessionReducer.ts` (pure, page-local `useReducer`; not Zustand, not
persisted): `{ modeId, context, inFlight: string | null, queued: string | null, last: ScanLogEntry | null, log: ScanLogEntry[] }`.
Actions: `setMode` (clears context, keeps log), `setContext`, `scanRequested` (if idle →
inFlight; if busy → replaces `queued`, max one), `scanSettled` (append log, set last, promote
queued), `clearLast`. Submissions are serialised: at most one request in flight. Log capped at
200 entries.

Feedback: result card + short WebAudio beep (success/error tones) with a toggle (default on;
preference in `localStorage`, wrapped in try/catch).

### 6.6 Security

No client-side trust: every scan hits existing endpoints that enforce permission, branch
access, state and custody server-side. UI filtering of modes/manifests/riders is ergonomic only.

## 7. Error / loading behaviour (summary)

- Every data block has skeleton + `ErrorState` with retry, isolated from siblings.
- Scan failures never throw out of the page; network errors become a rejected log entry
  ("Network error — not recorded, scan again").
- `ComingSoonSlot`s never fetch.

## 8. Testing

**Backend (xUnit, existing integration fixture, shared `courier-postgres-1`):**
- List filters: each narrows; combined AND; unknown `status` → 400; `q` length 1 → 400;
  `createdFrom > createdTo` → 400; `branchId` outside scope → empty page; merchant caller pinned
  to own merchant even with another `merchantId`; `lastEventAt` = latest event; unfiltered result
  identical to pre-change (items included — N+1 rewrite guard).
- `GET /{awb}`: in-scope staff 200; out-of-scope staff 404; all-branches staff 200; other tenant
  404; merchant own 200 / other 404.
- `GET /{awb}/events`: ascending order; no metadata/lat/long in payload; merchant 403; out of
  scope 404; other tenant 404; no `shipment.read` 403.

**Frontend (Vitest + Testing Library):**
- `statusGroups`: no `ShipmentStatus` is in more than one tab group, and the exact set in none
  (`Delivered`, `Cancelled`, international statuses) is pinned by the test.
- Dashboard: pipeline bucket counts + hrefs; KPI tiles without delta; Needs-action rows hidden
  per permission; hourly-flow slot present; one block erroring leaves others rendered; range
  control produces UTC-anchored `from`/`to`.
- Shipments list: tab → `status` params; URL round-trip; debounce (fake timers) and < 2 chars not
  sent; filter change resets page; filtered-empty vs empty; row click opens Sheet and sets
  `aria-selected`.
- Sheet: stepper for forward, NDR and RTO statuses; `buildCustodyChain` fixtures; "Staff"
  fallback without `user.read`; COD/Attempts/Audit slots; events error isolated; 404 message.
- Scan: `scanSessionReducer` transitions (in-flight + single queue); each mode's `submit` hits
  the right endpoint(s) with the right body; context required before scanning; continuous dedupe
  within 3 s (fake timers); rejected scan shows server message; unavailable modes hidden; rider
  PWA scanner tests unchanged and green from the new location.
- Bar: `dotnet build`, `dotnet test`, `npm run build`, `npm run lint`, `npm test`,
  `tsc --noEmit` (no new errors beyond the 8 pre-existing).

## 9. Documentation

- `docs/API-SPEC.md`: list filters, `lastEventAt`, events endpoint, 404 for out-of-scope reads.
- `docs/RBAC.md`: branch scope on single-shipment reads.
- `docs/CLARIFICATIONS.md`: #25 — branch scope on `GET /shipments/{awb}` (+ events), 404 not
  403, internal scan lookups unchanged.
- `docs/FRONTEND-ROADMAP.md`: R2 → Done with spec link; R2 backend follow-ups.
- `DESIGN.md`: scan station pattern (camera-first, result card, session log), progress stepper,
  "Right now" custody card.

## 10. Backend follow-ups surfaced by R2

1. Hourly booked/delivered flow.
2. Prior-period deltas for KPIs and pipeline.
3. Needs-action counts: manifests with missing parcels, riders holding cash > 24 h, SLA risk.
4. Per-branch on-hand / out-for-delivery counts.
5. COD split: rider-held vs deposited-unverified.
6. Per-shipment COD, delivery attempts, entity audit reads.
7. Standalone sort operation + sort bins.
8. "Next expected" (run stop, attempt N of M) for the custody card.
9. Bulk CSV shipment upload; label printing; rider reassignment.
10. Server-side actor display names on events.
11. "Needs action" shipments filter.

## 11. Delivery

Branch `feat/f15-r2-staff-operations` in a git worktree; subagent-driven execution per the
implementation plan; not merged until the user approves.
