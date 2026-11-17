# F15 R5b-1 — Merchant Portal Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the merchant dashboard, shipments list + detail Sheet, booking Sheet and COD page + settlement Sheet to the F15 artboards, backed by four new merchant-scoped COD reads.

**Architecture:** Backend adds read-only routes to the Cod module (summary, awaiting list, per-shipment COD status, settlement CSV), with two new cross-module lookups declared where the consumer can see them (time zone in SharedKernel, delivered-count in Cod) and `CsvWriter` moved to SharedKernel. Web adds a merchant status-group model, filterable list/count hooks, COD hooks and BFF routes, then rebuilds each page/Sheet from the R1 page primitives.

**Tech Stack:** ASP.NET Core + EF Core (Postgres), xUnit; Next.js + TypeScript + Tailwind + shadcn/ui, TanStack Query, Zustand, react-hook-form + zod, vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-11-17-f15-r5b1-merchant-core-design.md` (artboards: canvas row "Merchant portal" — `MerchantDashboard`, `MerchantShipments`, `MerchantBooking`, `MerchantCod`, `MerchantSidebar` at https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu)

## Global Constraints

- Every new route: merchant JWT or merchant API key, `RequireMerchantPermissionAnyCredential(MerchantPermissionCatalog.CodSettlementsRead)`, explicit `if (httpContext.User.GetMerchantId() is null) return Results.Forbid();`, self-scoped to `GetMerchantId()` plus explicit `OrganizationId == _currentTenantProvider.OrganizationId`. Other merchant's ids → 404.
- "Today"/"this month" = the organization's `OrganizationSettings.TimeZoneId` (default `Asia/Kathmandu`), never UTC calendar dates.
- Collected-not-settled predicate: `CODCollection.Status` ∈ {CodCollected, RiderDeposited, BranchReconciled, FinanceVerified} **and** `MerchantSettlementItemId == null`.
- Merchants never see staff/rider names, custody user ids, internal notes. Shipment journey for merchants = the public tracking read.
- Web: shadcn/ui components (Tabs, Pagination, DatePicker, Checkbox, Spinner, Skeleton, Sheet, AlertDialog, Select); detail/create views are right-side Sheets via the existing Zustand panel stores; only on-scale font sizes (`text-2xs`…, no `text-[..px]`); console tokens; amounts via `formatRs` (`features/cod/lib/codFormat.ts`).
- Counts that fail to load render "—", never 0.
- Git tracks the web app as lowercase `src/web` — always `git add src/web/...`. Web `tsc --noEmit` 0 errors; full vitest green (known flaky `syncManager.test`; 5 s timeouts under load → rerun those files alone).
- No new library, no migration.

## Review Focus

1. **Second merchant in the same org** calls every new route → sees none of the first merchant's money, parcels, settlement CSV or COD status (404 for ids). Tests in Tasks 2–3.
2. **Time-zone boundary**: a collection at 23:50 NPT yesterday vs 00:10 NPT today; a payout on the last NPT day of last month → correct buckets. Tests in Tasks 1–2.
3. **Prepaid shipment** (COD 0, no collection row): detail Sheet shows "Prepaid", no "You receive"; cod-status returns `Prepaid`. Tests in Tasks 2 and 8.
4. **A count request fails** while others succeed (e.g. one tab's list call 500s): that tab/tile shows "—", others render; dashboard "on the way" shows "—" rather than a wrong sum. Tests in Tasks 4 and 6.
5. **Settlement CSV with amounts that start with "-"** (or AWB with `=`): the formula guard must not corrupt numbers — fees are written as positive values in a "Courier fee" column, AWBs escaped. Test in Task 3.

---

### Task 1: Backend groundwork — CsvWriter to SharedKernel, org time zone, delivered-count lookup, day/month ranges

**Files:**
- Move: `src/Modules/Reports/Infrastructure/CsvWriter.cs` → `src/SharedKernel/CsvWriter.cs` (namespace `Courier.SharedKernel`); update `Modules/Reports/Application/ReportExportService.cs` and `tests/Reports.Tests/CsvWriterTests.cs` usings.
- Create: `src/SharedKernel/IOrganizationTimeZoneLookup.cs`
- Create: `src/Modules/Identity/Application/OrganizationTimeZoneLookup.cs`
- Create: `src/Modules/Cod/Application/IShipmentDeliveryLookupService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentReportingLookupService.cs` (add interface + method)
- Create: `src/Modules/Cod/Application/OrgCalendar.cs`
- Modify: `src/Api/Program.cs` (registrations next to line ~241)
- Test: `tests/Cod.Tests/OrgCalendarTests.cs`, `tests/Shipments.Tests/ShipmentDeliveryLookupTests.cs` (Postgres via `TestDbContextFactory`), `tests/Identity.Tests/OrganizationTimeZoneLookupTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace Courier.SharedKernel;
  public interface IOrganizationTimeZoneLookup { Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken ct = default); }

  namespace Courier.Cod.Application;
  public interface IShipmentDeliveryLookupService {
      // shipments of merchantId with a Delivered or DeliveredAbroad ShipmentEvent in [fromUtc, toUtc)
      Task<int> CountDeliveredAsync(Guid merchantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
      // true when the (tenant-filtered) shipment exists and Shipment.MerchantId == merchantId
      Task<bool> BelongsToMerchantAsync(Guid shipmentId, Guid merchantId, CancellationToken ct = default);
  }
  public static class OrgCalendar {
      public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) Today(TimeZoneInfo tz, DateTimeOffset nowUtc);
      public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ThisMonth(TimeZoneInfo tz, DateTimeOffset nowUtc);
  }
  ```
- `OrganizationTimeZoneLookup` reads `OrganizationSettings.TimeZoneId` for the org (via `DbContext`, `IgnoreQueryFilters` not needed — tenant context is the same org), resolves with `TimeZoneInfo.FindSystemTimeZoneById`; on missing row or unknown id falls back to `Asia/Kathmandu`, and if that id itself can't be resolved, to `TimeZoneInfo.CreateCustomTimeZone("Asia/Kathmandu", TimeSpan.FromMinutes(345), "Nepal Time", "Nepal Time")`.
- `CountDeliveredAsync` intersects `ShipmentEvent` (no tenant filter) with the tenant-filtered `Shipment` set on `MerchantId`; counts distinct shipments.

- [ ] **Step 1: Failing tests**
  - `OrgCalendarTests`: with Nepal tz and `nowUtc = 2026-09-30T18:30:00Z` (= 00:15 NPT 1 Oct) → `Today` = `[2026-09-30T18:15Z, 2026-10-01T18:15Z)`; `ThisMonth` = `[2026-09-30T18:15Z, 2026-10-31T18:15Z)`. With `nowUtc = 2026-09-30T18:00:00Z` (23:45 NPT 30 Sep) → `Today` = `[2026-09-29T18:15Z, 2026-09-30T18:15Z)`, `ThisMonth` starts `2026-08-31T18:15Z`.
  - `OrganizationTimeZoneLookupTests`: org with `TimeZoneId = "Asia/Kathmandu"` → `BaseUtcOffset == 5:45`; unknown id `"Mars/Base"` → 5:45 fallback.
  - `ShipmentDeliveryLookupTests`: merchant A has 2 shipments delivered inside the range, 1 delivered before `fromUtc`, 1 `DeliveredAbroad` inside; merchant B 1 inside → `CountDeliveredAsync(A, …) == 3`, `(B, …) == 1`; `BelongsToMerchantAsync(aShipment, A) == true`, `(aShipment, B) == false`, `(Guid.NewGuid(), A) == false`.
  - `CsvWriterTests` keep passing after the namespace move.
- [ ] **Step 2: Run, see fail** — `dotnet test tests/Cod.Tests --filter OrgCalendarTests` (compile error).
- [ ] **Step 3: Implement** the files above; register `IOrganizationTimeZoneLookup → OrganizationTimeZoneLookup` (scoped) and `IShipmentDeliveryLookupService → ShipmentReportingLookupService` (scoped, same instance pattern as line 241).
- [ ] **Step 4: Run** `dotnet build` (0 errors) and `dotnet test tests/Cod.Tests tests/Shipments.Tests tests/Reports.Tests tests/Identity.Tests` → PASS (Postgres :5434 up).
- [ ] **Step 5: Commit** `feat(api): org time zone lookup, delivered-count lookup and shared CsvWriter`

### Task 2: Backend — merchant summary, awaiting list, per-shipment COD status

**Files:**
- Create: `src/Modules/Cod/Application/CodService.MerchantPortal.cs` (partial)
- Modify: `src/Modules/Cod/Application/CodDtos.cs`, `CodService.cs` (ctor gains `IOrganizationTimeZoneLookup`, `IShipmentDeliveryLookupService`), `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Modify: every Cod.Tests `CreateService` (add `file class` fakes `FixedTimeZoneLookup` (Nepal) and `ZeroDeliveryLookup`)
- Test: `tests/Cod.Tests/CodServiceMerchantPortalTests.cs` (in-memory pattern of `CodServiceMerchantSettlementsTests`), `tests/Api.Tests/CodEndpointsTests.cs` (new facts reusing its private helpers)

**Interfaces:**
- Consumes: Task 1 interfaces and `OrgCalendar`.
- Produces (JSON camelCase):
  ```csharp
  public record MoneyBucket(decimal Amount, int Count);
  public record SettledBucket(decimal Amount, int Payouts, DateTimeOffset? LastSettledAt);
  public record DeliveredTodayBucket(int Parcels, decimal CodCollected);
  public record MerchantCodSummaryResponse(MoneyBucket CollectedNotSettled, MoneyBucket InSettlement, SettledBucket SettledThisMonth, DeliveredTodayBucket DeliveredToday);
  public record MerchantAwaitingCollectionResponse(Guid ShipmentId, string Awb, DateTimeOffset CollectedAt, decimal CollectedAmount, decimal CourierFee, decimal Net);
  [JsonConverter(typeof(JsonStringEnumConverter))] public enum MerchantCodState { Prepaid, NotCollected, CollectedAwaitingSettlement, InSettlement, Settled }
  public record MerchantShipmentCodStatusResponse(MerchantCodState State, Guid? SettlementId, DateTimeOffset? PeriodFrom, DateTimeOffset? PeriodTo, DateTimeOffset? SettledAt);
  // CodService
  Task<MerchantCodSummaryResponse> GetMerchantCodSummaryAsync(ClaimsPrincipal actingUser, DateTimeOffset nowUtc, CancellationToken ct = default);
  Task<PagedResult<MerchantAwaitingCollectionResponse>> ListMerchantAwaitingAsync(int? page, int? pageSize, ClaimsPrincipal actingUser, CancellationToken ct = default);
  Task<MerchantShipmentCodStatusResponse?> GetMerchantShipmentCodStatusAsync(Guid shipmentId, ClaimsPrincipal actingUser, CancellationToken ct = default);
  ```
- Routes (new group `/api/v1/cod` merchant group, same auth chain as `merchantGroup` at CodEndpoints.cs:113): `GET /merchant-summary`, `GET /merchant-awaiting?page&pageSize`, `GET /merchant-shipments/{shipmentId:guid}/cod-status`; endpoint passes `DateTimeOffset.UtcNow`.
- Rules: summary `CollectedNotSettled` = Σ `CollectedAmount` / count over the predicate; `InSettlement` = Σ `TotalPayable` / count of `Created` settlements; `SettledThisMonth` over `Settled` with `SettledAt` in `ThisMonth`; `DeliveredToday.Parcels` = `CountDeliveredAsync(merchant, Today)`, `CodCollected` = Σ `CollectedAmount` of the merchant's collections with `CollectedAt` in `Today`. Awaiting: predicate, `OrderByDescending(CollectedAt).ThenBy(Id)`, `PaginationDefaults.Normalize`, AWBs via `IShipmentAwbLookupService`, `CourierFee = ShipmentTotalCharge`, `Net = CollectedAmount − CourierFee`. COD status: `BelongsToMerchantAsync(shipmentId, caller)` false → `null` (404); otherwise query `CODCollection` by `ShipmentId` + caller `MerchantId`; none → `Prepaid`; `CodExpected` → NotCollected; collected states with null item → CollectedAwaitingSettlement; item set → join item → settlement: `Created` → InSettlement, `Settled` → Settled with period/settledAt/id.

- [ ] **Step 1: Failing tests**
  - `CodServiceMerchantPortalTests`: seed for merchant A: collections in CodCollected (Rs 1,000), FinanceVerified (Rs 500), CodExpected (Rs 700, excluded), one MerchantSettlementCreated with item (excluded); merchant B CodCollected Rs 9,999 → summary for A `CollectedNotSettled == (1500, 2)`; a `Created` settlement Rs 4,820 → `InSettlement == (4820, 1)`; Settled at 2026-09-30T18:20Z (= 00:05 NPT 1 Oct) and one at 2026-09-30T18:10Z with `nowUtc` 2026-10-05 → `SettledThisMonth.Payouts == 1`, `LastSettledAt == 2026-09-30T18:20Z`; collection `CollectedAt` 23:50 NPT yesterday excluded and 00:10 NPT today included in `DeliveredToday.CodCollected`.
  - Awaiting: A sees 2 rows newest first with `Net == CollectedAmount − ShipmentTotalCharge`; B's rows absent; the three service methods throw `CodValidationException("Merchant context required.")` for a principal without `merchant_id` (the endpoint returns 403 before reaching it — covered in Api tests).
  - COD status: CodExpected → `NotCollected`; collected no item → `CollectedAwaitingSettlement`; settled → `Settled` + `SettledAt`; another merchant's shipment → `null`; caller's shipment with no collection → `Prepaid`.
  - `CodEndpointsTests` (reuse `CreateMerchantWithLoginAsync`, `LoginMerchantAsync`, `BookAndCompleteCodDeliveryAsync`, `SeedMerchantSettlementAsync`): `MerchantSummary_MerchantJwt_SeesOwnCollectedAmount` (after one Rs 2,500 COD delivery `collectedNotSettled.amount == 2500`, `count == 1`); `MerchantSummary_SecondMerchantSameOrg_SeesZero`; `MerchantSummary_StaffToken_Returns403`; `MerchantAwaiting_ListsOwnCollection_WithAwb`; `MerchantCodStatus_ForeignShipment_Returns404`; `MerchantCodStatus_AfterSettlement_ReturnsInSettlement`.
- [ ] **Step 2: Run, see fail.**
- [ ] **Step 3: Implement** service partial, DTOs, routes; update all existing `CreateService` call sites in Cod.Tests.
- [ ] **Step 4: Run** `dotnet test tests/Cod.Tests` and `dotnet test tests/Api.Tests --filter FullyQualifiedName~CodEndpointsTests` → PASS (Postgres + Redis up).
- [ ] **Step 5: Commit** `feat(api): merchant COD summary, awaiting list and per-shipment COD status`

### Task 3: Backend — settlement CSV export

**Files:**
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs` (route on `merchantGroup`: `GET /api/v1/cod/settlements/{id:guid}/export`)
- Create: `src/Modules/Cod/Application/MerchantSettlementCsv.cs` — `public static IEnumerable<string> Lines(MerchantSettlementResponse settlement)`
- Test: `tests/Cod.Tests/MerchantSettlementCsvTests.cs`, `tests/Api.Tests/CodEndpointsTests.cs`

**Interfaces:**
- Consumes: `CsvWriter.WriteLines` (Task 1), existing `GetMerchantSettlementByIdAsync`.
- CSV: headers exactly `AWB,COD amount,Courier fee,Other deductions,Net payable`; one row per item with amounts formatted `0.00` invariant culture, fee and deductions as **positive** numbers; final row `Total,{ΣCOD},{Σfee},{Σother},{TotalPayable}`. Response `text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="settlement-{PeriodFrom:yyyyMMdd}-{PeriodTo:yyyyMMdd}.csv"`, streamed like `ReportsEndpoints.HandleCsvAsync`. Not found / other merchant → 404; staff → 403.

- [ ] **Step 1: Failing tests** — `MerchantSettlementCsvTests`: two items (COD 2500 fee 150; COD 990 fee 100) → lines `["AWB,COD amount,Courier fee,Other deductions,Net payable\r\n", "NX-1,2500.00,150.00,0.00,2350.00\r\n", "NX-2,990.00,100.00,0.00,890.00\r\n", "Total,3490.00,250.00,0.00,3240.00\r\n"]`; an AWB `"=HYPERLINK"` is written guarded (`'=HYPERLINK`). Api: `SettlementExport_Merchant_ReturnsCsvWithHeaderAndTotal` (content type starts `text/csv`, disposition contains `settlement-`, body first line is the header, last line starts `Total,`); `SettlementExport_ForeignSettlement_Returns404`; `SettlementExport_StaffToken_Returns403`.
- [ ] **Step 2–4:** run fail → implement → `dotnet test tests/Cod.Tests --filter MerchantSettlementCsvTests` and the Api filter → PASS.
- [ ] **Step 5: Commit** `feat(api): merchant settlement CSV export`

### Task 4: Web — merchant status groups, list params, list + count hooks, status labels, nav badge

**Files:**
- Create: `src/web/src/features/merchant-shipments/lib/statusGroups.ts` (+ `.test.ts`)
- Create: `src/web/src/features/merchant-shipments/lib/listParams.ts` (+ `.test.ts`)
- Modify: `src/web/src/features/merchant-shipments/hooks/useMerchantShipments.ts`; create `hooks/useMerchantShipmentCounts.ts` (+ test)
- Modify: `src/web/src/components/status-badge/statusConfig.ts` (+ test) — new `Merchant.` namespace
- Modify: `src/web/src/components/merchant-layout/useMerchantNavGroups.ts` (+ test)
- Modify: existing callers of `useMerchantShipments(page, pageSize)` (shipments page, `MerchantDashboardStats`) to the new signature so tsc stays clean

**Interfaces:**
- Produces:
  ```ts
  export type MerchantStatusGroup = "booked" | "inTransit" | "outForDelivery" | "needsAttention" | "returning" | "delivered";
  export const MERCHANT_STATUS_GROUPS: Record<MerchantStatusGroup, { label: string; statuses: readonly string[] }>; // exact spec table
  export type MerchantShipmentTab = "all" | MerchantStatusGroup;
  export const MERCHANT_SHIPMENT_TABS: readonly MerchantShipmentTab[]; // all, booked, inTransit, outForDelivery, needsAttention, returning, delivered
  export function groupOf(status: string): MerchantStatusGroup | null; // Cancelled → null

  export interface MerchantShipmentListParams { tab: MerchantShipmentTab; q: string; from: string | null; to: string | null; serviceTypeId: string | null; page: number } // from/to "yyyy-MM-dd"
  export const EMPTY_MERCHANT_LIST_PARAMS: MerchantShipmentListParams;
  export function parseMerchantListParams(sp: URLSearchParams): MerchantShipmentListParams; // unknown tab → "all", bad page → 1
  export function toMerchantSearchParams(p: MerchantShipmentListParams): URLSearchParams; // omits defaults
  export function toMerchantApiQuery(p: MerchantShipmentListParams, pageSize: number): URLSearchParams; // repeated status=, q, serviceTypeId, createdFrom/createdTo as UTC ISO of the local day bounds via lib/dates/rangePresets toUtcRange, page, pageSize

  useMerchantShipments(params: MerchantShipmentListParams, pageSize = 20) // queryKey ["merchant-shipments", query], keepPreviousData
  useMerchantShipmentCounts(): Record<MerchantShipmentTab, { count: number | null; isLoading: boolean }> // one pageSize=1 request per tab, count = totalCount, null on error
  ```
- `statusConfig.ts` `Merchant.` labels: Created/PickupAssigned "Booked" (`status-created`); PickedUp "Picked up"; AtOriginBranch/Sorted/InTransit and the six international transit statuses "In transit" (`status-in-transit`); AtDestinationBranch "At destination branch" (`status-in-transit`); OutForDelivery "Out for delivery" (`status-out-for-delivery`); DeliveryFailed/NdrCreated/InternationalException "Delivery failed" (`status-ndr`); RtoInitiated/RtoInTransit/RtoAtOrigin "Returning to you" (`status-rto`); RtoDelivered "Returned to you" (`status-rto`); Delivered/DeliveredAbroad "Delivered" (`status-delivered`); Cancelled "Cancelled" (`status-cancelled`). Check `StatusBadge` accepts a namespace prop; add one if missing (default unchanged).
- Nav badge: `useMerchantNavGroups` adds `badge` to the Shipments item = needsAttention count when the user has `shipment.read` and count > 0 (query `["merchant-shipments","count","needsAttention"]` shared with `useMerchantShipmentCounts`); otherwise no badge.

- [ ] **Step 1: Failing tests** — statusGroups: every `ShipmentStatus` name except Cancelled maps to exactly one group (list the 26 names in the test); `groupOf("RtoDelivered") === "returning"`, `groupOf("Cancelled") === null`. listParams: round-trip; `toMerchantApiQuery({tab:"inTransit",…}, 20).getAll("status")` equals the 11 inTransit names; `tab=bogus` → "all". counts: mocked fetch where the `needsAttention` request 500s → that entry `count === null`, others numbers. statusConfig: `resolveStatusConfig("RtoDelivered","Merchant").label === "Returned to you"`. nav: badge `3` shown with permission + count 3; absent at 0 and without `shipment.read`.
- [ ] **Step 2–4:** fail → implement → `npx vitest run src/features/merchant-shipments src/components/status-badge src/components/merchant-layout` and `npx tsc --noEmit` → PASS / 0 errors.
- [ ] **Step 5: Commit** `feat(web): merchant status groups, filterable list and count hooks, nav badge`

### Task 5: Web — COD + tracking data layer (BFF routes, hooks)

**Files:**
- Create BFF: `src/web/src/app/api/merchant/cod/summary/route.ts`, `cod/awaiting/route.ts` (forwards query), `cod/shipments/[id]/status/route.ts`, `cod/settlements/[id]/export/route.ts` (streams body, passes `Content-Type`/`Content-Disposition`, mirror `app/api/reports/export/cod-summary/route.ts` with `merchantApiFetch`); route tests next to each (pattern of `app/api/merchant/shipments/route.test.ts`)
- Create hooks in `src/web/src/features/merchant-cod/hooks/`: `useMerchantCodSummary.ts`, `useMerchantAwaiting.ts`, `useMerchantShipmentCodStatus.ts`; types in `features/merchant-cod/types.ts`
- Modify: `src/web/src/features/tracking/hooks/useTrackShipment.ts` — `useTrackShipment(awb: string, options?: { poll?: boolean })`, default `poll: true`; `poll: false` → `refetchInterval: false`

**Interfaces:**
- Produces: `useMerchantCodSummary(): UseQueryResult<MerchantCodSummary>`; `useMerchantAwaiting(page: number, pageSize = 20): UseQueryResult<PagedResult<MerchantAwaitingCollection>>`; `useMerchantShipmentCodStatus(shipmentId: string | undefined): UseQueryResult<MerchantShipmentCodStatus>` (disabled without id); `settlementExportUrl(id: string): string` = `/api/merchant/cod/settlements/${id}/export`. TS types mirror Task 2 records (camelCase; `state` string union).

- [ ] **Step 1: Failing tests** — each route forwards to the exact upstream path (`/api/v1/cod/merchant-summary`, `/api/v1/cod/merchant-awaiting?page=2&pageSize=20`, `/api/v1/cod/merchant-shipments/{id}/cod-status`, `/api/v1/cod/settlements/{id}/export`), returns 401 when `merchantApiFetch` throws; export route preserves `content-disposition`. `useTrackShipment(awb, { poll: false })` sets no refetch interval (assert via query options or fake timers: one fetch after 30 s).
- [ ] **Step 2–4:** fail → implement → `npx vitest run src/app/api/merchant src/features/merchant-cod src/features/tracking` + tsc → PASS.
- [ ] **Step 5: Commit** `feat(web): merchant COD BFF routes and hooks`

### Task 6: Web — merchant dashboard

**Files:**
- Modify: `src/web/src/app/merchant/dashboard/page.tsx`
- Create: `src/web/src/features/merchant-dashboard/components/MoneyBand.tsx`, `ParcelsNow.tsx`, `NeedsAttention.tsx`, `RecentShipments.tsx` (+ `MerchantDashboard.test.tsx`)
- Delete: `MerchantDashboardStats.tsx` + its test

**Interfaces:** consumes Task 4 counts/list hooks, Task 5 summary hook, `useMerchantShipmentPanelStore.openCreate/openDetail`, `useMerchantPickupPanelStore.openCreate`, `useMerchantPermission`, `useMerchantSession` (`fullName`, `merchantDisplayName`), `PageHeader`, `SectionCard`, `KpiTile`, `formatRs`.

Layout and copy per spec "Dashboard": title `Good morning|afternoon|evening, {first name}` (local hour: <12 morning, <17 afternoon, else evening), description `{merchant} · {n} parcels on the way` (n = booked+inTransit+outForDelivery; "—" if any is null); actions "Request pickup" (`merchant.pickups.create`… use the permission string the pickup create Sheet already checks) and "Book shipment" (`shipment.create`). Money band labels: "Collected, not yet settled" (primary-filled tile), "In settlement", "Settled this month" with captions `{count} delivered COD parcels`, `{n} settlement(s) awaiting payout`, `{payouts} payouts · last on {d MMM}`; hidden without `merchant.cod.read`. Parcels tiles Booked / In transit / Out for delivery / Delivered today (caption `{formatRs(codCollected)} COD collected`), each a link to `/merchant/shipments?tab=…` (Delivered today → `tab=delivered`). Needs attention: "{n} failed deliveries" → `?tab=needsAttention`, "{n} returning to you" → `?tab=returning`; both 0 → "Nothing needs you right now". Recent: latest 7 (`EMPTY_MERCHANT_LIST_PARAMS`, pageSize 7), row click → `openDetail(awb)`.

- [ ] **Step 1: Failing tests** — with mocked hooks: greeting uses first name; money band shows `Rs 61,450`; tiles show counts and link hrefs; needsAttention 0 and returning 0 → "Nothing needs you right now"; booked count null → description shows "— parcels on the way" (Review Focus 4); no `merchant.cod.read` → money band absent; clicking a recent row calls `openDetail` with its AWB.
- [ ] **Step 2–4:** fail → implement → `npx vitest run src/features/merchant-dashboard src/app/merchant` + tsc → PASS.
- [ ] **Step 5: Commit** `feat(web): merchant dashboard with money, parcels and attention`

### Task 7: Web — merchant shipments page

**Files:**
- Modify: `src/web/src/app/merchant/shipments/page.tsx`
- Create: `src/web/src/features/merchant-shipments/components/MerchantShipmentsView.tsx` (+ test), `MerchantShipmentsTable.tsx`, `useMerchantListUrlState.ts` (mirror `features/shipments/components/ShipmentsView.tsx` URL handling)

**Interfaces:** consumes Task 4 (`parseMerchantListParams`, `toMerchantSearchParams`, `useMerchantShipments`, `useMerchantShipmentCounts`, `MERCHANT_SHIPMENT_TABS`, `MERCHANT_STATUS_GROUPS`), `useMerchantBranches`, `useMerchantServiceTypes`, `StatusBadge` with `Merchant` namespace, shadcn Tabs/Pagination/DatePicker/Select, `formatRs`.

Header: "Shipments", description `{all count} shipments booked · {on the way} on the way`; actions as dashboard. Tabs with counts (needsAttention count styled destructive when > 0). Filters: search input (debounced 300 ms → `q`), DatePicker "Booked from"/"to", service Select ("Any"). Columns per spec (Destination = destination branch name or "—"; COD "Prepaid" when 0; Updated = `lastEventAt ?? createdAt`). Changing tab/filter resets page to 1. Row click → `openDetail(awb)`. Empty state per tab: "No shipments here yet". Error → `ErrorState` with retry.

- [ ] **Step 1: Failing tests** — `?tab=needsAttention` selects that tab and requests the 3 needsAttention statuses; typing in search updates the URL `q` after debounce and resets `page`; a shipment with `codAmount 0` shows "Prepaid"; unknown destination branch id shows "—"; a count that is null renders "—" in its tab.
- [ ] **Step 2–4:** fail → implement → `npx vitest run src/features/merchant-shipments src/app/merchant/shipments` + tsc → PASS.
- [ ] **Step 5: Commit** `feat(web): merchant shipments list with status tabs and filters`

### Task 8: Web — shipment detail Sheet

**Files:**
- Modify: `src/web/src/features/merchant-shipments/components/MerchantShipmentDetailSheet.tsx`
- Create: `src/web/src/features/merchant-shipments/lib/codLine.ts` (+ test), `components/MerchantShipmentDetailSheet.test.tsx`

**Interfaces:** consumes `useMerchantShipment(awb)`, `useMerchantShipmentCodStatus(shipment.id)` (Task 5), `useTrackShipment(awb, { poll: false })` + `buildJourney` + `TrackingJourney` (R5a), `SheetHeaderBlock`, `useCancelMerchantShipment`, `useMerchantPermission("shipment.cancel")`, shadcn Tabs/AlertDialog.
- Produces: `codLine(status: MerchantShipmentCodStatus | undefined, codAmount: number): string` — `codAmount === 0` → "Prepaid"; NotCollected → "Not yet collected"; CollectedAwaitingSettlement → "Collected, awaiting settlement"; InSettlement → `In settlement ({d MMM}–{d MMM})`; Settled → `Settled on {d MMM yyyy}`; undefined → "—".

Header eyebrow `AWB · {SERVICE NAME UPPER} · COD|PREPAID`, facts COD to collect / Your charge (`totalCharge`) / You receive (`codAmount − totalCharge`, omitted when prepaid) / Weight. "Where it is now": Status (Merchant label), Delivering branch, Last update, COD settlement (`codLine`). Stepper + Journey tab from the tracking read; tracking error → Journey tab shows "Journey unavailable right now" with retry, rest of Sheet unaffected. Tabs Journey · Parcel · Recipient · Charges. Footer: Cancel (Booked group + permission, AlertDialog confirm "Cancel this shipment? This can't be undone."), "Copy tracking link" (`navigator.clipboard.writeText(\`${window.location.origin}/track/${awb}\`)`, toast "Tracking link copied").

- [ ] **Step 1: Failing tests** — `codLine` cases above (no month-text assertions beyond `toMatch(/^Settled on /)`); Sheet: prepaid shipment shows "Prepaid" and no "You receive" (Review Focus 3); Delivered shipment hides Cancel; Created shipment with permission shows Cancel; tracking 429 → "Journey unavailable right now" while facts still render; no staff/rider names are rendered (fixture event metadata absent by construction — assert `queryByText(/Rider/)` null).
- [ ] **Step 2–4:** fail → implement → `npx vitest run src/features/merchant-shipments` + tsc → PASS.
- [ ] **Step 5: Commit** `feat(web): merchant shipment detail sheet with journey and COD line`

### Task 9: Web — booking Sheet restructure

**Files:**
- Modify: `src/web/src/features/merchant-shipments/components/MerchantShipmentCreateSheet.tsx`, `MerchantItemsField.tsx`
- Create: `components/BookingPriceFooter.tsx`, `components/ServiceTypeCards.tsx`, `components/MerchantShipmentCreateSheet.test.tsx`

**Interfaces:** consumes existing `useForm`/schema, `useMerchantShipmentQuote`, `useCreateMerchantShipment`, `useMerchantServiceTypes`, `useMerchantBranches`, geography pickers already in the Sheet.

Sections `01 · RECIPIENT`, `02 · PARCEL`, `03 · SERVICE AND PAYMENT` (mono eyebrows), service types as a radio-card group (shadcn RadioGroup if present in `components/ui`, else accessible `role="radiogroup"` of labelled radio inputs — check `components/ui` first), branch selects labelled "Pickup from" / "Deliver from", insurance as shadcn Checkbox "Insure this parcel", sender summary line `Sender: {senderName} · {senderPhone} · change` where "change" reveals the existing sender fields. Footer: Delivery · Insurance · Your charge · You receive (from the quote; "—" until quoted) and primary button `Book for {formatRs(total)}` (text "Book shipment" and disabled until a quote exists). Validation, idempotency key, submit flow unchanged.

- [ ] **Step 1: Failing tests** — renders the three section headings; button disabled with "Book shipment" before a quote; with mocked quote total 150 and COD 2500 → footer "You receive Rs 2,350" and button "Book for Rs 150"; clicking "change" reveals the sender name input; submitting an empty form still shows the existing validation messages.
- [ ] **Step 2–4:** fail → implement → `npx vitest run src/features/merchant-shipments` + tsc → PASS.
- [ ] **Step 5: Commit** `feat(web): sectioned booking sheet with live price footer`

### Task 10: Web — COD page, settlement Sheet, docs

**Files:**
- Modify: `src/web/src/app/merchant/cod/page.tsx`, `features/merchant-cod/components/MerchantSettlementDetailSheet.tsx`
- Create: `features/merchant-cod/components/CodMoneyStrip.tsx`, `SettlementsTable.tsx`, `AwaitingTable.tsx`, `features/merchant-cod/lib/settlementTotals.ts` (+ tests), `components/MerchantCodPage.test.tsx`
- Delete `features/merchant-cod/lib/toCsv.ts` + test only if nothing imports it after this task (grep first)
- Modify: `docs/FRONTEND-ROADMAP.md` (F15 table: R5b-1 Done with spec link; R5b-2 "Pickups, KYC, staff, API keys — restyle with page primitives" Planned)

**Interfaces:** consumes Task 5 hooks + `settlementExportUrl`, `useMerchantSettlements`, `useMerchantSettlement`, `useMerchantSettlementPanelStore.openDetail`, `PipelineStrip` or `KpiTile` row, shadcn Tabs/Select.
- Produces: `settlementTotals(items: MerchantSettlementItem[]): { cod: number; fees: number; other: number; net: number }`; `filterByPeriod(settlements: MerchantSettlement[], period: "30d" | "90d" | "all", now: Date): MerchantSettlement[]` (by `periodTo`).

Strip: three steps "Collected, not yet settled" (highlighted) › "In settlement" › "Settled this month" from the summary. URL tabs `?tab=settlements|awaiting`. Settlements columns Period (`d MMM–d MMM yyyy`) · Parcels (`items.length`) · COD collected · Fees · You receive · Status (`MerchantSettlement.` labels "Awaiting payout"/"Settled") · Paid on; period Select "Last 30 days / Last 90 days / All" default 90d. Awaiting columns AWB · Collected on · COD · Fee · Net, paged. Sheet: eyebrow `SETTLEMENT · {period}`, title `formatRs(totalPayable)`, status, `{n} delivered COD parcels`; "How it adds up" rows from `settlementTotals`; items table; footer `<a href={settlementExportUrl(id)} download>` styled as outline Button "Download CSV".

- [ ] **Step 1: Failing tests** — `settlementTotals` sums; `filterByPeriod` keeps a settlement with `periodTo` 20 days ago for 30d and drops one 40 days ago; page: `?tab=awaiting` shows the awaiting table; settlement row click opens the Sheet; Sheet "You receive" equals `net` and the Download link href is `/api/merchant/cod/settlements/{id}/export`; summary error → strip values "—".
- [ ] **Step 2–4:** fail → implement → full `npx vitest run`, `npx tsc --noEmit`, `npx next lint --dir src/app/merchant --dir src/features` → PASS / 0 errors.
- [ ] **Step 5: Update `docs/FRONTEND-ROADMAP.md`** as listed.
- [ ] **Step 6: Commit** `feat(web): merchant COD page and settlement sheet with CSV download`
