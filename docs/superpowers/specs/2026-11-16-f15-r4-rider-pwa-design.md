# F15 Console Redesign — R4: Rider PWA (Run, Proof of delivery, Scan, Profile) — Design Spec

## 1. Context

R4 is the "Rider PWA" row of the F15 console redesign: run screen, POD flow, scan and profile.
R3b-2 merged on 2026-11-16 at `74e156c`.

Design source: the "Finance, field and public" row of the canvas
https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu:
- `RiderTasks.dc.html` — today's run.
- `RiderPod.dc.html` — proof of delivery.
- `RiderScan.dc.html` and `RiderProfile.dc.html` — drawn for R4 (2026-11-16). Publishing them to the
  canvas was blocked by the session's permission mode, so the user approved the local drafts instead
  (saved in the session scratchpad); they can be added to the canvas later.

Decisions made while brainstorming (user-approved):
1. **Artboards first** for scan and profile (approved as local drafts).
2. **Backend scope.** One self-scoped, read-only read, `GET /api/v1/riders/me/today`. No migrations,
   no new entities, no new writes. (The user answered "continue implementation" to the A/B question;
   the recommended option A was taken and is recorded here so it can be overruled.)
3. **POD is a full-height bottom Sheet**, laid out like the artboard, following the project rule that
   detail/create views are Sheets driven by a Zustand store, not routed pages.
4. **The offline outbox and its behaviour do not change.** Every write still goes through the existing
   outbox hooks (`useRiderPickupScan`, `useCompleteDelivery`, `useRecordFailedAttempt`).

### Scope boundary (same as R2, R3a, R3b)
- Frontend restyle plus the read in §3. No writes beyond the existing ones.
- Data the API does not serve renders `ComingSoonSlot`, a disabled control with "Needs backend", or is
  left off. Values are never mocked.
- shadcn/ui only. On-scale type classes only. Status is never conveyed by colour alone. Touch targets
  are at least 44 px.
- Loading and error states never show `0`. Use skeletons, `ErrorState` or "—".

### Code facts this spec follows (code wins over the artboards)
- **Today's tasks.** `GET /riders/me/tasks/today` returns only `Pending` tasks of today's `Active`
  run, and reads shipment details one task at a time (`IShipmentTaskLookupService.GetTaskInfoAsync`).
  It stays unchanged (the outbox and existing screens rely on it).
- **Today** is the UTC date, `DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date)`, as everywhere else
  in Riders (R3b-2 follow-up 1 covers Nepal-local time).
- **OTP.** `DeliveryProof.OtpVerified` is a client-reported boolean. Nothing sends an SMS or checks a
  code. The POD step is therefore a "Customer confirmed with OTP" checkbox.
- **Payment.** Only cash is recorded (`CollectedCodAmount`). Fonepay QR has no backend.
- **Cash held by a rider** = `CodCollection` rows with status `CodCollected` and
  `CollectedByRiderId` = the rider — the same rule as `CodService.GetRiderCashAsync` and the deposit
  sweep. Riders do not hold `cod.read`, so the rider's own figure needs a self-scoped read.
- **Rider name** comes from the session (`/auth/me` `fullName`); no backend change.
- **Run code** ("DR-0930-07"), **distance**, **service type chip**, **"Attempt 1 of 3"** and
  **"Deposit by 7:00 PM"** have no backing data and are left off (§9 follow-ups).

## 2. Artboard elements vs data source

| Artboard element | Source |
|---|---|
| Header greeting name | session `fullName` |
| Header branch | `me/today` `branchName` |
| Delivered ‹done› / ‹total› + bar | `me/today` `counts.completed`, `counts.assigned` |
| Cash in hand | `me/today` `cash`; null → "No cash held" |
| Tabs To deliver / Pickups / Done | `me/today` `tasks` grouped by type and status |
| Next stop card | first `Pending` task by `sequence` |
| COD chip / Prepaid | task `codAmount` (> 0 → "COD Rs X", else "Prepaid") |
| AWB tail | task `awb` (last 5 characters, prefixed "…") |
| Call / Map | `tel:` link; Google Maps search link built from address + ward |
| Distance, Express, Attempt N of 3, run code, deposit deadline | left off (§9) |
| POD recipient card | task recipient name, phone, address |
| POD GPS line | browser geolocation already captured by the POD form |
| POD Collect COD | task `codAmount`; cash only; Fonepay disabled with "Needs backend" |
| POD OTP | "Customer confirmed with OTP" checkbox (`otpVerified`) |
| POD proof | existing signature pad, photo input, recipient name |
| Scan camera + typed AWB | existing `BarcodeScanner` (`manualEntry`) |
| Scan last result / not on run / recent scans | matched against `me/today` tasks; status from the outbox |
| "‹n› waiting to sync" | `useOutboxStatus` pending count |
| Profile today + cash | `me/today` |
| Profile needs review / sync | `useOutboxStatus` stuck and pending items; existing retry and drain |
| Profile details | `me/today` `branchName`, `vehiclePlate`; `/riders/me` `licenseNumber` |
| Open staff console | shown when the session holds any permission outside the rider set |

## 3. Backend slice (read-only)

### 3.1 `GET /api/v1/riders/me/today`

Requires authentication only — the same self-scoped idiom as `/riders/me` and `/riders/me/tasks/today`.
The rider is resolved from the JWT's user id; no rider id is accepted from the client. A signed-in user
with no `Rider` row gets **404**. Merchant callers get **403** (`GetMerchantId() is not null`).

```csharp
public record MyTodayTask(
    Guid TaskId, Guid ShipmentId, string Awb, DeliveryTaskType Type, int Sequence, DeliveryTaskStatus Status,
    string RecipientName, string RecipientPhone, string RecipientAddressLine, int RecipientWardNumber, decimal CodAmount);
public record MyTodayCounts(int Assigned, int Completed, int Failed);
public record MyCashHeld(decimal Amount, int ParcelCount, DateTimeOffset OldestCollectedAt);
public record MyTodayResponse(
    RiderBoardRunSummary? Run, IReadOnlyList<MyTodayTask> Tasks, MyTodayCounts Counts,
    MyCashHeld? Cash, string? BranchName, string? VehiclePlate);
```

- **Run.** Today's runs for the rider; pick with the existing `PickTodayRun` rule (Active first, then
  highest `Id`). No run → `Run` null, empty `Tasks`, zero counts.
- **Tasks.** All tasks of that run (every status), ordered by `Sequence`. Shipment details come from a
  new **batched** `IShipmentTaskLookupService.GetTaskInfosAsync(IReadOnlySet<Guid> shipmentIds)`
  returning `IReadOnlyDictionary<Guid, ShipmentTaskInfo>` (one query; empty input → no query). A task
  whose shipment is missing is skipped, as `GetMyTasksTodayAsync` does.
- **Counts.** Over the returned tasks: Assigned = all, Completed, Failed.
- **Cash.** New `Courier.Cod.Application.ICodRiderCashLookupService.GetHeldByRiderAsync(Guid riderId)`
  → `RiderHeldCash?` (amount, count, oldest), implemented in Cod with the `GetRiderCashAsync` row rule
  (status `CodCollected`, `CollectedByRiderId == riderId`, summed across branches; `CollectedAt ?? CreatedAt`
  for oldest). No rows → null. Tenant scope is the `CodCollection` query filter.
- **BranchName.** `IBranchLookupService.GetNamesByIdsAsync({ rider.BaseBranchId })`.
- **VehiclePlate.** First vehicle with `RiderId == rider.Id`, by `PlateNumber` (same rule as the board).
- A fixed number of queries per request.

### 3.2 Errors
- 401 unauthenticated, 403 merchant caller, 404 no rider for this user.

## 4. Shared web pieces
- **Types** in `features/riders/types.ts`: `MyTodayTask`, `MyToday` (camelCase of §3.1).
- **Proxy** `app/api/riders/me/today/route.ts`.
- **Hook** `features/rider-app/hooks/useMyToday.ts`, key `["riders","me","today"]`. Every outbox
  enqueue/drain that already invalidates `["riders","me","tasks","today"]` also invalidates this key.
- **Pure model** `features/rider-app/lib/runModel.ts`:
  - `nextStop(tasks)` — first `Pending` by `sequence`.
  - `groupRunTasks(tasks)` → `{ toDeliver, pickups, done }` (done = Completed or Failed).
  - `awbTail(awb)` → "…" + last 5 characters.
  - `mapsHref(addressLine, ward)` → Google Maps search URL.
  - `cashMatch(expected, entered)` → `{ kind: "match" | "short" | "over", difference }`.
  - `taskSyncState(shipmentId, outbox)` → `"synced" | "waiting" | "needs-review"`.
  - `hasStaffPermissions(permissions)` → true when any permission is outside
    `delivery.attempt`, `delivery.complete`, `cod.collect`, `shipment.scan` (the seeded DeliveryRider
    and PickupRider sets in `AuthService.RegisterOrganizationAsync`).
- **Panel store.** `riderAppPanelStore` keeps `openRecordAttempt(task)`; the POD Sheet opens on it.
  It gains a `mode: "deliver" | "fail"` so "Report issue" and "Couldn't deliver" open straight to the
  failure form.
- **Rider shell.** `RiderShell` bottom nav keeps three tabs, relabelled Run / Scan / Profile, with
  ≥44 px targets and `aria-current="page"`.

## 5. Run screen (`app/rider/tasks/page.tsx`)

- **Header** (dark band):
  - "Run · ‹branchName›" small line; "Namaste, ‹first name›" title.
  - Delivered card: "‹completed› / ‹assigned›" with a progress bar; "No run today" when `run` is null.
  - Cash card: amount (`formatRs`), "from ‹n› COD parcels"; "No cash held" when `cash` is null.
  - Loading: skeletons in both cards; error: `ErrorState` with retry for the whole screen.
- **Tabs** ("To deliver · ‹n›", "Pickups · ‹n›", "Done · ‹n›") as shadcn `Tabs`, kept in component state.
- **Next stop card** (To deliver and Pickups tabs, first pending of that tab):
  - Eyebrow "NEXT STOP"; sequence badge; recipient name; address and ward.
  - Chips: "COD Rs X" or "Prepaid"; AWB tail.
  - Buttons: Call (`tel:`), Map (`mapsHref`, opens a new tab), primary "Deliver" (opens POD) or
    "Scan pickup" (existing `useRiderPickupScan`).
  - Sync state text: "Saved · waiting to sync" or "Needs review — see Profile".
- **Other rows:** sequence, name, area line, COD/Prepaid text; tapping a delivery row opens POD.
- **Done tab:** rows with "Delivered" or "Failed" in words.
- **Empty states:** "No run today" (no run), "Nothing left to deliver" (tab empty).
- **Bottom action bar** above the nav: "Scan parcel" (links to `/rider/scan`) and an icon button
  "Report issue" (`aria-label`) that opens the failure form for the next delivery stop; disabled when
  there is none.

## 6. Proof of delivery (`RiderPodSheet`, replaces `RiderRecordAttemptSheet`)

Full-height bottom Sheet. Two modes in one component: **deliver** (default) and **fail**.

- **Header:** back button (closes), "Complete delivery", AWB · "stop ‹sequence›".
- **Recipient card:** name, `tel:` phone, address; GPS line "GPS ‹lat›, ‹lng› ±‹m› m" once captured,
  "Getting location…" while pending, "Location unavailable" on error (never blocks submit, as today).
- **1 · Collect COD** (only when `codAmount > 0`):
  - Amount due.
  - Method toggle: Cash (selected); "Fonepay QR" disabled with a "Needs backend" tooltip.
  - "Cash received" input; helper text from `cashMatch`: "Matches", "Short by Rs X", "Over by Rs X".
- **2 · OTP:** checkbox "Customer confirmed with OTP"; helper "SMS codes need backend support".
- **3 · Proof:** photo button (existing validation), signature pad with "Clear", "Received by" input.
- **Footer:** primary "Confirm delivered" (+ " · Rs X collected" for COD); secondary "Couldn't deliver"
  switches to fail mode.
- **Fail mode:** reason select (existing reasons), notes, "Customer contacted" checkbox, next action;
  "Record failed attempt"; "Back to delivery".
- **Validation and submit** are unchanged from `RiderRecordAttemptSheet` (recipient name required,
  COD amount required and ≥ 0 when COD, photo type/size checks) and use the same outbox hooks.

## 7. Scan (`app/rider/scan/page.tsx`)

- **Header** (dark): "‹branchName› · ‹n› to deliver · ‹m› pickups"; title "Scan parcel".
- **Scanner:** existing `BarcodeScanner` (camera + typed AWB), restyled into the dark band.
- **Result card** for the latest scan:
  - Pickup task → runs the pickup scan; card "PICKED UP", AWB, "Pickup from ‹name›, ‹address›", and
    "Saved on this phone · waiting to sync" until the outbox reports it synced ("Synced").
  - Delivery task → opens the POD Sheet; card "OPENED DELIVERY".
  - Unknown AWB → warning card "‹AWB› isn't on your run today" with "Hand it to the branch counter,
    or ask dispatch to assign it to you."
- **Recent scans:** this visit's scans (component state, newest first, max 10) with status words:
  Synced / Waiting to sync / Needs review / Opened delivery / Not on run.
- **Sync bar:** "‹n› scans waiting to sync · Sends when you're online" when the outbox has pending items.

## 8. Profile (`app/rider/profile/page.tsx`)

- **Header** (dark): initials avatar, session `fullName`, "‹role› · ‹branchName›".
- **Today card:** "‹completed› of ‹assigned› done", "‹failed› failed" (only when > 0); cash in hand with
  "‹n› COD parcels · oldest ‹age›" and "Hand cash over at the ‹branchName› COD counter before you sign
  off."; "No cash held" when null.
- **Needs review card** (only when stuck items exist): count, each item's kind + AWB (AWB from today's
  tasks by shipment id, else the short shipment id), the server's message, and "Try again" (existing
  retry).
- **Sync card:** "All changes sent" or "‹n› waiting"; explanation line; "Send now" (existing drain).
- **Details:** base branch, vehicle plate (or "No vehicle"), licence, "Location · Shared while open".
- **Buttons:** "Open staff console" (`/dashboard`, only when `hasStaffPermissions`), "Sign out".

## 9. Tests, exit criteria, follow-ups

### Tests
- **Backend unit (InMemory):** self-scope (another rider's tasks and cash never returned); no rider →
  null; no run → empty; all statuses returned in sequence order; counts; batched task-info lookup called
  once; missing shipment skipped; cash null when nothing held; vehicle plate rule.
- **Lookup tests (Postgres):** `GetTaskInfosAsync` (tenant-filtered, empty input), `GetHeldByRiderAsync`
  (only `CodCollected` for that rider, summed across branches, other tenant excluded).
- **API tests (Postgres):** 200 for a rider, 401, 404 for a staff user with no rider, 403 merchant,
  tenant isolation.
- **Web:** `runModel` unit tests; proxy and hook; run screen (header loading/error, tabs, next stop
  actions, sync states, empty states); POD Sheet (deliver/fail modes, COD match text, Fonepay disabled,
  validation, submit through the outbox hooks); scan (pickup/delivery/unknown results, recent list,
  sync bar); profile (today/cash, needs review retry, sync, staff-console gating).

### Exit criteria
- The four artboards are implemented to §2's data rules, the read in §3 is self-scoped and tested,
  the offline outbox behaves as before, and docs are updated (API-SPEC, FRONTEND-ROADMAP R4 row and
  follow-ups). Full verification: `dotnet build`, the listed test projects, web `tsc` 0 errors, lint,
  `npm test`, `npm run build`. Live check on a phone-width window by the user.

### Non-goals
- Sending or verifying SMS OTPs; Fonepay or any non-cash payment; attempt limits; route order or
  distance; push notifications.

### Backend follow-ups (log in FRONTEND-ROADMAP F15)
1. SMS OTP send and verify for delivery.
2. Non-cash COD payment (Fonepay QR) recorded on delivery.
3. Delivery attempt limit ("Attempt N of M").
4. Recipient coordinates and distance to the next stop.
5. Branch cash deposit deadline for riders.
6. A human-readable run code.
