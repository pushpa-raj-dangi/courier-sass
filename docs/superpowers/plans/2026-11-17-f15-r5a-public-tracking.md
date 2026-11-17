# F15 R5a — Public Tracking Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild public `/track` and `/track/[awb]` to the F15 "Public tracking (mobile)" artboard, backed by two new safe public fields (destination area, sender = merchant display name).

**Architecture:** Additive change to `PublicTrackingService` / `PublicTrackingResponse` (no endpoint, no migration). Web: a pure `buildJourney()` helper turns the API result into six done/now/next steps; a new `TrackingJourney` component renders them; the result page and landing page move from the marketing palette to the console/app design tokens.

**Tech Stack:** ASP.NET Core + EF Core (Postgres), xUnit; Next.js + TypeScript + Tailwind + shadcn/ui, TanStack Query, vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-11-17-f15-r5a-public-tracking-design.md`

## Global Constraints

- Public response may add only `destinationArea` and `senderName`. Never expose street address, phone, recipient/sender person names, COD/charges, staff/rider/branch ids, notes, metadata (CLAUDE.md §24).
- `senderName` = `Merchant.DisplayName`, never `Shipment.SenderName`.
- `destinationArea` = `"{LocalLevel.Name}, {District.Name}"`; district alone if only district; else `RecipientCity`; else `null`.
- "Arriving today" only for `OutForDelivery`; no clock time anywhere in the headline.
- No stop number, OTP, COD, reschedule or support buttons.
- Tailwind: only on-scale font sizes (`text-2xs` … ), no arbitrary `text-[..px]` (DESIGN.md On-Scale Rule). shadcn/ui components (`Input`, `Button`, `Skeleton`, `Label`) over raw primitives.
- Tests must not assert ICU month text (this Node renders en-GB "Sept").
- Web app path is lowercase `src/web` in git — always `git add src/web/...`.
- Web `tsc --noEmit` must show 0 errors; full vitest green.

## Review Focus

1. **Reattempt after a failed delivery** (events: … OutForDelivery → DeliveryAttemptUnsuccessful → OutForDelivery): the Out-for-delivery step is `now`, not alert, and shows the *latest* OFD event time. Test in Task 2.
2. **Missing geography / international inbound** (`destinationArea` null, or both fields null): the card line drops the missing part and renders nothing when both are null — no "To null", no dangling "·". Tests in Task 1 (formatter) and Task 4 (page).
3. **Cancelled before pickup**: only reached steps render (Received done), no `now`/`next` steps, cancelled headline. Test in Task 2.
4. **Background refetch fails after data was shown** (TanStack keeps `data` and sets `isError`): page keeps showing the parcel, not the error state. Test in Task 4.
5. **Skipped stages** (Delivered with no AtDestinationBranch event, e.g. collapsed or international inbound): earlier stages still show `done` with no time, never `next`. Test in Task 2.

---

### Task 1: Backend — destinationArea and senderName on the public response

**Files:**
- Modify: `src/Modules/Shipments/Application/PublicTrackingDtos.cs:27`
- Modify: `src/Modules/Shipments/Application/PublicTrackingService.cs`
- Create: `tests/Shipments.Tests/PublicTrackingAreaFormatterTests.cs`
- Modify: `tests/Api.Tests/PublicTrackingEndpointTests.cs`

**Interfaces:**
- Produces: `public record PublicTrackingResponse(string Awb, PublicTrackingStatus CurrentStatus, List<PublicTrackingEvent> Events, string? DestinationArea, string? SenderName);` — JSON `destinationArea`, `senderName`.
- Produces: `public static class PublicTrackingAreaFormatter { public static string? Format(string? localLevelName, string? districtName, string? city); }` in `PublicTrackingDtos.cs` (or its own file in `Application/`).

- [ ] **Step 1: Write failing unit tests** (`PublicTrackingAreaFormatterTests`, `[Theory]`):
  - `("Lalitpur Metropolitan City", "Lalitpur", null)` → `"Lalitpur Metropolitan City, Lalitpur"`
  - `(null, "Lalitpur", null)` → `"Lalitpur"`
  - `(null, null, "Sydney")` → `"Sydney"`
  - `("X", "Y", "Sydney")` → `"X, Y"` (geography wins)
  - `(null, null, null)` and `(null, null, "  ")` → `null`
  - `("X", null, null)` → `"X"`
- [ ] **Step 2: Add failing API assertions** in `PublicTrackingEndpointTests`:
  - New `[Fact] GetTrack_DomesticShipment_ReturnsDestinationAreaAndMerchantDisplayName`: book via `BookAndMoveToDestinationAsync`, anon GET → `tracking.DestinationArea == "Kathmandu Metropolitan City, Kathmandu"`, `tracking.SenderName == "Shipper"` (the `CreateMerchantAsync` display name).
  - Extend `GetTrack_ResponseBody_NeverContainsInternalOrFinancialFieldsAtWireLevel`: also assert the lower-cased body does not contain `"recipient one"`, `"sender one"`, `"9800000000"`, `"9800000001"`, `"recipient address"`, `"sender address"`, `"shipper pvt"` (legal name), `"recipientphone"`, `"addressline"`.
- [ ] **Step 3: Run to see them fail**
  Run: `dotnet test tests/Shipments.Tests --filter PublicTrackingAreaFormatterTests` → compile failure / FAIL.
- [ ] **Step 4: Implement.** Append the two fields to the record; add the formatter (trim, treat whitespace as null). In `GetByAwbAsync` after picking `shipment`: load `Merchant.DisplayName` with `IgnoreQueryFilters()` by `shipment.MerchantId`; load `LocalLevel.Name` / `District.Name` by `RecipientLocalLevelId` / `RecipientDistrictId` when set (Geography entities, `IgnoreQueryFilters()` harmless if none exist); pass `shipment.RecipientCity` as city. Keep the fail-closed duplicate rule and the existing comments.
- [ ] **Step 5: Run tests**
  Run: `dotnet test tests/Shipments.Tests --filter PublicTracking` then `dotnet test tests/Api.Tests --filter PublicTrackingEndpointTests` (start Docker Desktop / courier-postgres-1 on 5434 and Redis first).
  Expected: all PASS.
- [ ] **Step 6: Commit**
  ```bash
  git add src/Modules/Shipments/Application tests/Shipments.Tests/PublicTrackingAreaFormatterTests.cs tests/Api.Tests/PublicTrackingEndpointTests.cs
  git commit -m "feat(api): destination area and merchant name on public tracking"
  ```

### Task 2: Web — tracking types, headlines and buildJourney()

**Files:**
- Modify: `src/web/src/features/tracking/types.ts`
- Modify: `src/web/src/features/tracking/lib/statusMeta.ts`
- Create: `src/web/src/features/tracking/lib/journey.ts`
- Create: `src/web/src/features/tracking/lib/journey.test.ts`

**Interfaces:**
- Produces in `types.ts`: `PublicTrackingResult` gains `destinationArea: string | null; senderName: string | null;`.
- Produces in `statusMeta.ts`: `StatusMeta` gains `headline: string`. Exact copy:
  Received "Booked, waiting for pickup" · PickedUp "Picked up from the seller" · InTransit "On its way" · AtDestinationBranch "At your local branch" · OutForDelivery "Arriving today" · Delivered "Delivered" · DeliveryAttemptUnsuccessful "We missed you" · Cancelled "Shipment cancelled". Remove `railStageIndex` (no remaining users after Task 4).
- Produces in `journey.ts`:
  ```ts
  export type JourneyStepState = "done" | "now" | "next";
  export interface JourneyStep {
    stage: JourneyStage;            // one of JOURNEY_STAGES
    label: string;
    state: JourneyStepState;
    alert: boolean;                 // true only for a failed attempt on the OFD step
    timestamp: string | null;       // ISO of the LATEST event for this stage, null if none
    location: string | null;
  }
  export const JOURNEY_STAGES = ["Received","PickedUp","InTransit","AtDestinationBranch","OutForDelivery","Delivered"] as const;
  export type JourneyStage = (typeof JOURNEY_STAGES)[number];
  export function buildJourney(result: PublicTrackingResult): JourneyStep[];
  export function formatEventTime(iso: string): string; // Intl "en-GB", weekday short, day numeric, month short, hour numeric, minute 2-digit
  ```

Rules for `buildJourney` (the tests pin them):
- Current stage index: `DeliveryAttemptUnsuccessful` → OutForDelivery's index; `Cancelled` → no current stage; otherwise the stage equal to `currentStatus`.
- Stages before current → `done`; current → `now` (`Delivered` current → `done`, there is no `now`); after → `next`.
- `DeliveryAttemptUnsuccessful` current: OFD step `now`, `alert: true`, label "Delivery attempt unsuccessful", timestamp/location from the latest `DeliveryAttemptUnsuccessful` event.
- `Cancelled`: return only stages that have an event, all `done`.
- Labels: "Order received", "Picked up from seller", "In transit", AtDestinationBranch → `Arrived at ${location}` when its event has a location else "Arrived at local branch", "Out for delivery", "Delivered".

- [ ] **Step 1: Write failing tests** in `journey.test.ts` (build fixtures with a small `result(currentStatus, events)` helper):
  - `received only` → 6 steps; [0] `now`, [1..5] `next`; `alert` all false.
  - `at destination with location "Lalitpur Branch"` → [0..2] done, [3] now label "Arrived at Lalitpur Branch".
  - `reattempt`: events Received, PickedUp, InTransit, AtDestinationBranch, OFD(t1), DeliveryAttemptUnsuccessful(t2), OFD(t3), current OFD → [4] `now`, `alert` false, `timestamp === t3`.
  - `failed attempt current` → [4] `now`, `alert` true, label "Delivery attempt unsuccessful", `timestamp === t2`; [5] `next`.
  - `delivered with no AtDestinationBranch event` → all 6 `done`; [3].timestamp `null`; label "Arrived at local branch".
  - `cancelled after Received only` → exactly 1 step, `Received`, `done`.
  - `formatEventTime("2026-09-29T05:17:00Z")` matches `/\d{1,2}:\d{2}/` (no month assertion).
- [ ] **Step 2: Run to see them fail**
  Run: `cd src/web && npx vitest run src/features/tracking/lib/journey.test.ts` → FAIL (module not found).
- [ ] **Step 3: Implement** types, headlines and `journey.ts`.
- [ ] **Step 4: Run tests**
  Run: `npx vitest run src/features/tracking` → PASS (TrackingTimeline tests still pass; it is removed in Task 4).
- [ ] **Step 5: Commit**
  ```bash
  git add src/web/src/features/tracking
  git commit -m "feat(web): tracking journey model and status headlines"
  ```

### Task 3: Web — search form and /track landing restyle

**Files:**
- Modify: `src/web/src/features/tracking/TrackingSearchForm.tsx`
- Modify: `src/web/src/features/tracking/TrackingSearchForm.test.tsx`
- Modify: `src/web/src/app/track/page.tsx`
- Modify: `src/web/src/app/track/page.test.tsx`
- Create: `src/web/src/features/tracking/components/TrackingHeader.tsx`

**Interfaces:**
- Produces: `TrackingSearchForm({ defaultAwb?: string })` — artboard search row: one bordered `h-12` field with search icon, mono `Input` (`aria-label="AWB number"`, placeholder `NP2026-000123`), and a `Button` "Track" inside the field on the right. Validation/`FieldError` and `router.push(/track/{encoded trimmed awb})` unchanged. Console tokens only (`bg-card`, `border-input`, `text-muted-foreground`, `bg-primary`), no `marketing-*` classes.
- Produces: `TrackingHeader({ showBack?: boolean })` — `h-14` bar: brand link to `/` ("Courier Platform", Package mark), optional "Track another parcel" link to `/track` (≥44px target), `MarketingThemeToggle` on the right.

- [ ] **Step 1: Update tests first**
  - `TrackingSearchForm.test.tsx`: keeps the existing submit/validation cases; add `prefills defaultAwb` (`getByLabelText("AWB number")` has value `"NX-1"` when `defaultAwb="NX-1"`) and `trims and encodes on submit` (`" ab 1 "` → push `/track/ab%201`).
  - `app/track/page.test.tsx`: replace the marketing-grid assertion with: one h1 "Track a parcel"; the AWB field present; text "No account needed" present; `container.querySelector('[data-testid="marketing-grid"]')` is `null`.
- [ ] **Step 2: Run to see them fail** — `npx vitest run src/features/tracking/TrackingSearchForm.test.tsx src/app/track/page.test.tsx` → FAIL.
- [ ] **Step 3: Implement.** Landing: `bg-background min-h-screen`, `TrackingHeader`, centred `max-w-md` column: mono eyebrow "TRACK A PARCEL" (`text-2xs tracking-[0.14em]`), h1 "Track a parcel" (`font-display text-3xl`), helper "Enter the AWB number from your receipt or SMS.", the form, footnote "No account needed — tracking is public for every shipment." Drop framer-motion/grid/glow.
- [ ] **Step 4: Run tests** — same command → PASS.
- [ ] **Step 5: Commit**
  ```bash
  git add src/web/src/features/tracking src/web/src/app/track/page.tsx src/web/src/app/track/page.test.tsx
  git commit -m "feat(web): console-styled track search and landing page"
  ```

### Task 4: Web — /track/[awb] result page per artboard, docs

**Files:**
- Create: `src/web/src/features/tracking/components/TrackingStatusCard.tsx`
- Create: `src/web/src/features/tracking/components/TrackingJourney.tsx`
- Create: `src/web/src/features/tracking/components/TrackingJourney.test.tsx`
- Modify: `src/web/src/app/track/[awb]/page.tsx`
- Modify: `src/web/src/app/track/[awb]/page.test.tsx`
- Delete: `src/web/src/features/tracking/components/TrackingTimeline.tsx`, `TrackingTimeline.test.tsx`
- Modify: `docs/FRONTEND-ROADMAP.md` (F15 table: replace the R5 row with "R5a — Public tracking | `/track`, `/track/[awb]` | Done — spec `docs/superpowers/specs/2026-11-17-f15-r5a-public-tracking-design.md`" and "R5b — Merchant portal | 7 merchant pages (artboards first) | Planned"; add "Backend follow-ups from R5a": ETA window, delivery OTP hint once SMS OTP ships, reschedule request, org support contact on tracking)

**Interfaces:**
- Consumes: `buildJourney`, `formatEventTime`, `JourneyStep` (Task 2); `STATUS_META[status].headline` (Task 2); `TrackingHeader`, `TrackingSearchForm({ defaultAwb })` (Task 3); `resolveStatusConfig(status, "Public")` → `{ label, token }` (existing).
- Produces: `TrackingStatusCard({ result }: { result: PublicTrackingResult })`, `TrackingJourney({ steps }: { steps: JourneyStep[] })` (`<section aria-label="Journey">`, an `<ol>`, each step `<li data-state={state}>`).

Layout (artboard): header → search form (prefilled with the AWB) → status card (`bg-card border rounded-lg` with offset shadow `shadow-[6px_6px_0_0_hsl(var(--primary)/0.18)]`): status dot + label from `resolveStatusConfig` coloured by its `token`; h1 = headline; line "To {destinationArea} · from {senderName}" built from the non-null parts only (omit line when both null). Then journey (done = filled primary dot + primary line; now = status-token dot with ring; alert uses `status-ndr`; next = hollow dot, muted text, no line after last). Meta under each label: `formatEventTime(timestamp)` + ` · {location}` when present; nothing for `next`. Footer: border-top, "Tracking by Courier Platform · updated {HH:MM}" from `dataUpdatedAt` (`Intl` en-GB hour/minute). Column `max-w-md mx-auto px-4`, `min-h-screen bg-background`.

States: loading → `Skeleton`s; error with no `data` → existing not-found / rate-limited ("Try again in N seconds") / generic messages in a dashed bordered box; **data present → always render data, even if `isError`**.

- [ ] **Step 1: Write failing tests**
  - `TrackingJourney.test.tsx`: renders 6 `li`s for a Received-only journey with `data-state` `now,next,next,next,next,next`; a `next` step shows no time; an alert step's label "Delivery attempt unsuccessful" is visible.
  - `page.test.tsx` (keep its existing `useTrackShipment` mock style; update the success fixture with `destinationArea: "Jawalakhel, Lalitpur"`, `senderName: "Everest Organics"`):
    - success OFD → h1 "Arriving today"; text "To Jawalakhel, Lalitpur · from Everest Organics"; `getByRole("region", { name: "Journey" })`; text matching `/updated/i`.
    - `destinationArea: null` → text "from Everest Organics" present and no text matching `/To\s/`; both null → neither "from" nor "·" rendered in the card.
    - InTransit current → h1 "On its way" and no "Arriving today".
    - `data` present **and** `isError: true` with a generic error → parcel headline shown, "Something went wrong" not shown.
    - keep loading / not found / rate-limited cases (rate-limited still shows the seconds).
    - page source has no "OTP", "stop", "Change delivery time", "Contact support" text (assert `queryByText` null for each).
- [ ] **Step 2: Run to see them fail** — `npx vitest run src/app/track src/features/tracking` → FAIL.
- [ ] **Step 3: Implement** components and page; delete `TrackingTimeline*`; remove `LifecycleRail`/`WALKTHROUGH_STOPS`/`TRACKING_TONE_COLOR`/`toneAlpha` imports from the page (delete those helpers from `statusMeta.ts` only if nothing else imports them — `grep -rn` first).
- [ ] **Step 4: Verify**
  Run: `npx vitest run` → all PASS (note the known-flaky `syncManager.test`; rerun once if it alone fails).
  Run: `npx tsc --noEmit` → 0 errors.
  Run: `npx next lint --dir src/app/track --dir src/features/tracking` → no errors.
- [ ] **Step 5: Update `docs/FRONTEND-ROADMAP.md`** as listed under Files.
- [ ] **Step 6: Commit**
  ```bash
  git add src/web/src/features/tracking src/web/src/app/track docs/FRONTEND-ROADMAP.md
  git commit -m "feat(web): artboard tracking result page with journey and status card"
  ```
