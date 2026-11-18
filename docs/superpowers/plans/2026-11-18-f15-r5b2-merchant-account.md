# F15 R5b-2 — Merchant Portal Account & Setup Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle the merchant Pickups, KYC, Staff and API keys pages to the console artboards, backed by KYC review dates/rejection reason and a pickup list that names the branch, lists the parcels and derives its stage from them.

**Architecture:** Merchants module: three nullable columns on `MerchantKycDocument` (one migration), a required reason on reject, a separate merchant-facing DTO without the reviewer id. Shipments module: a pure `PickupStageRules` domain function, batched enrichment inside `PickupRequestService` (one shipment query, one branch-name lookup, one local-level + one district query per call) and a parcel-based cancel guard. Web: pure `lib/` derivations per feature (unit-tested), then pages and Sheets rebuilt from the R1 page primitives.

**Tech Stack:** ASP.NET Core + EF Core (Postgres), xUnit; Next.js + TypeScript + Tailwind + shadcn/ui, TanStack Query, Zustand, vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-11-17-f15-r5b2-merchant-account-design.md` (artboards: canvas https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu, row "Merchant portal: account and setup" — `MerchantPickups`, `MerchantKyc`, `MerchantStaff`, `MerchantApiKeys`).

## Global Constraints

- No new routes, no new library. Exactly one migration (Task 1). Existing permission filters stay: `PickupsRead`/`pickup.read`, `merchant.kyc.review`, `merchant.staff.manage`, `merchant.apikeys.manage`, `merchant.pickups.create|cancel`.
- Merchant responses never carry staff/rider identity: the merchant KYC DTO has no `ReviewedByUserId`; pickup parcels carry only `id, awb, destinationArea, codAmount`.
- Pickup enrichment never widens visibility: parcels are joined with `s.MerchantId == pickup.MerchantId` under the existing tenant query filter. An id that doesn't resolve is omitted, not an error.
- All server timestamps `DateTime.UtcNow` (existing style in both services).
- Web "today"/"this month"/relative day labels use the Kathmandu calendar (`CONSOLE_TIME_ZONE` from `components/console/formatDualDate.ts`), never the browser's local date or UTC. `requestedForDate` is a plain `yyyy-MM-dd` date — compare it as a date string, never via `new Date(...)`.
- Web: shadcn/ui only (Tabs, Sheet, AlertDialog, Table, Badge, Skeleton, Spinner); detail views are right-side Sheets via the existing Zustand panel stores; console tokens; on-scale font sizes only (`text-2xs` … no `text-[..px]`); amounts via `formatRs`, dates via `formatDate` (`features/cod/lib/codFormat.ts`). Page primitives: `PageHeader`, `KpiTile`, `SectionCard`, `SheetHeaderBlock` (`components/page/`), `BorderedTableContainer`, `StatusBadge`, `LoadingState`/`ErrorState`/`EmptyState`.
- Counts that fail to load render "—", never 0.
- Git tracks the web app as lowercase `src/web` — always `git add src/web/...`. Web `npx tsc --noEmit` 0 errors; vitest with `--maxWorkers=2`, one suite at a time (known flaky `syncManager.test`; 5 s timeouts under load → rerun that file alone). Backend: reuse the shared `courier-postgres-1` container (port 5434).

## Review Focus

1. **Legacy KYC rows** (`UploadedAt == null`, `ReviewedAt == null`) next to a fresh re-upload of the same type → the fresh one wins the latest-per-type pick; the row says "Uploaded —" not "Invalid Date". Test in Task 6.
2. **Pickup whose parcels were all cancelled**, or whose ids no longer resolve → stage `Requested`, `shipments: []`, parcels cell "0 parcels", no crash, still cancellable. Tests in Tasks 2, 3 and 4.
3. **Cancel race**: the merchant opened the Sheet while Requested; a rider collected a parcel before they clicked → the server 400 message ("A rider has already collected parcels from this pickup") is shown in the Sheet, the Sheet stays open, the list refetches. Tests in Tasks 3 and 5.
4. **Kathmandu date boundary**: at 23:30 UTC on 31 Oct (05:15 NPT 1 Nov) a pickup dated 1 Nov is "Today" and counts toward November's "Collected this month"; one dated 31 Oct with an open stage is "Date passed". Test in Task 4.
5. **Revoke a key used 5 minutes ago**: the dialog adds the "still calling it" line; a key last used 2 days ago or never does not; the revoked key moves to the Revoked tab with its revoked date and no action. Tests in Task 8.

---

### Task 1: KYC review dates, rejection reason, merchant DTO (backend)

**Files:**
- Modify: `src/Modules/Merchants/Domain/MerchantKycDocument.cs` (add `DateTime? UploadedAt`, `DateTime? ReviewedAt`, `string? RejectionReason`)
- Modify: `src/Modules/Merchants/Infrastructure/MerchantKycDocumentConfiguration.cs` (`RejectionReason` `HasMaxLength(500)`)
- Modify: `src/Modules/Merchants/Application/MerchantKycDtos.cs`
- Modify: `src/Modules/Merchants/Application/MerchantKycService.cs`
- Modify: `src/Modules/Merchants/Presentation/MerchantKycEndpoints.cs` (return types only)
- Create: migration `AddKycReviewDetails` in `src/Persistence/Migrations/`
- Test: `tests/Merchants.Tests/MerchantKycServiceTests.cs`, `tests/Api.Tests/MerchantKycEndpointsTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  // staff (review endpoint) response
  public record MerchantKycDocumentResponse(Guid Id, KycDocumentType DocumentType, KycStatus Status, Guid? ReviewedByUserId,
      DateTime? UploadedAt, DateTime? ReviewedAt, string? RejectionReason);
  // merchant-facing (GET/POST /api/v1/merchant/kyc-documents) response — no reviewer id
  public record MerchantPortalKycDocumentResponse(Guid Id, KycDocumentType DocumentType, KycStatus Status,
      DateTime? UploadedAt, DateTime? ReviewedAt, string? RejectionReason);
  public record ReviewKycDocumentRequest(KycStatus Status, string? RejectionReason = null);
  // MerchantKycService
  Task<MerchantPortalKycDocumentResponse> UploadDocumentAsync(...same params...);
  Task<List<MerchantPortalKycDocumentResponse>> ListDocumentsAsync(ClaimsPrincipal actingUser, CancellationToken ct = default);
  Task<MerchantKycDocumentResponse?> ReviewDocumentAsync(...same params...);
  ```
  JSON (camelCase): merchant list items `{ id, documentType, status, uploadedAt, reviewedAt, rejectionReason }`.

- [ ] **Step 1: Write the failing service tests** (add a `file class CapturingAuditLogger : IAuditLogger` holding `List<AuditEntry> Entries`)
  - `UploadDocumentAsync_SetsUploadedAt` — `UploadedAt` within 5 s of `DateTime.UtcNow`; `ReviewedAt` and `RejectionReason` null.
  - `ReviewDocumentAsync_RejectWithoutReason_Throws` — `new ReviewKycDocumentRequest(KycStatus.Rejected, "   ")` and `(KycStatus.Rejected, null)` both throw `MerchantValidationException`; document stays `Pending`.
  - `ReviewDocumentAsync_RejectReasonOver500_Throws` — 501 chars throws.
  - `ReviewDocumentAsync_RejectWithReason_StoresTrimmedReasonReviewedAtAndAudit` — reason `"  Photo is blurry  "` stored as `"Photo is blurry"`; `ReviewedAt` set; single audit entry `merchant_kyc.reviewed` whose `Before` contains `"Pending"` and `After` contains `"Rejected"` and `"Photo is blurry"`.
  - `ReviewDocumentAsync_ApproveAfterReject_ClearsReason` — reject with reason, then approve with reason `"ignored"` → `Status == Approved`, `RejectionReason == null`, `ReviewedAt` updated.
  - Existing `ReviewDocumentAsync_WrongOrganization_ReturnsNull` stays green.

- [ ] **Step 2: Run to verify they fail**
  Run: `dotnet test tests/Merchants.Tests --filter "FullyQualifiedName~MerchantKycServiceTests"`
  Expected: compile errors / FAIL on the new members.

- [ ] **Step 3: Implement**
  - Upload sets `UploadedAt = DateTime.UtcNow`.
  - Review: validate `Enum.IsDefined` first (existing), then for `Rejected` trim the reason; blank → `MerchantValidationException("A rejection reason is required.")`; length > 500 → `MerchantValidationException("Rejection reason must be 500 characters or fewer.")`. `Approved`/`Pending` set `RejectionReason = null`. Set `ReviewedAt = DateTime.UtcNow`, `ReviewedByUserId` as today. Audit `Before`/`After` are `JsonSerializer.Serialize(new { status, rejectionReason })` of the old and new values.
  - Two private mappers `ToStaffResponse` / `ToPortalResponse`.
  - Re-review of an already-reviewed document stays allowed.

- [ ] **Step 4: Add the migration**
  Run: `dotnet ef migrations add AddKycReviewDetails --project src/Persistence/Persistence.csproj --startup-project src/Api/Api.csproj`
  Inspect: exactly three `AddColumn` calls on `MerchantKycDocuments` (two `timestamp with time zone` nullable, one `character varying(500)` nullable), no other table touched, no backfill.

- [ ] **Step 5: API tests** in `MerchantKycEndpointsTests.cs`
  - `ListKycDocuments_ReturnsNewFields_AndNoReviewerId` — after upload + staff reject with reason, the merchant GET JSON item has `uploadedAt`, `reviewedAt`, `rejectionReason == "<reason>"` and **no** `reviewedByUserId` property (assert on `JsonElement.TryGetProperty` returning false).
  - `ReviewKycDocument_RejectWithoutReason_Returns400` — body `{ "status": "Rejected" }` → 400 with `error.code == "MERCHANT_VALIDATION_FAILED"`.
  - Existing cross-org review → 404 test stays green.

- [ ] **Step 6: Run** `dotnet test tests/Merchants.Tests` and `dotnet test tests/Api.Tests --filter "FullyQualifiedName~MerchantKyc"` — all PASS.

- [ ] **Step 7: Commit** `feat(merchants): KYC review reason, upload/review dates, merchant DTO without reviewer`

---

### Task 2: `PickupStageRules` domain function

**Files:**
- Create: `src/Modules/Shipments/Domain/PickupStage.cs`
- Test: `tests/Shipments.Tests/PickupStageRulesTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  [JsonConverter(typeof(JsonStringEnumConverter))]
  public enum PickupStage { Requested, Scheduled, Collected, Cancelled }
  public static class PickupStageRules
  {
      // true for every status except Created, PickupAssigned, Cancelled
      public static bool IsPastPickup(ShipmentStatus status);
      public static PickupStage Derive(PickupRequestStatus stored, IReadOnlyCollection<ShipmentStatus> parcels);
  }
  ```

- [ ] **Step 1: Write the failing tests** (`[Theory]`s)
  - Stored `Cancelled` → `Cancelled` regardless of parcels (including all `Delivered`).
  - Stored `Completed` → `Collected` regardless of parcels (including empty).
  - Stored `Requested` or `Scheduled`:
    - `[]` → `Requested`; `[Cancelled, Cancelled]` → `Requested`.
    - `[Created, Created]` → `Requested`.
    - `[Created, PickupAssigned]` → `Scheduled`; `[PickupAssigned]` → `Scheduled`.
    - `[Created, PickedUp]` → `Scheduled`.
    - `[PickedUp, Delivered]` → `Collected`; `[PickedUp, Cancelled]` → `Collected` (cancelled parcel ignored).
    - `[RtoInitiated]` → `Collected`; one international post-pickup status (pick any `ShipmentStatus` member added in I0–I4) → `Collected`.
  - `IsPastPickup`: a test that iterates `Enum.GetValues<ShipmentStatus>()` and asserts it is false exactly for `Created`, `PickupAssigned`, `Cancelled` — so a future status defaults to "past pickup".

- [ ] **Step 2: Run** `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~PickupStageRulesTests"` — FAIL (type missing).
- [ ] **Step 3: Implement** per spec §3 (stored overrides first; filter out `Cancelled` parcels; empty → Requested; all past → Collected; any `PickupAssigned` or past → Scheduled; else Requested).
- [ ] **Step 4: Run** the same filter — PASS.
- [ ] **Step 5: Commit** `feat(shipments): derive pickup stage from its parcels`

---

### Task 3: Pickup list enrichment and parcel-based cancel guard

**Files:**
- Modify: `src/Modules/Shipments/Application/PickupRequestDtos.cs`
- Modify: `src/Modules/Shipments/Application/PickupRequestService.cs` (inject `IBranchLookupService`)
- Test: `tests/Shipments.Tests/PickupRequestServiceTests.cs` (apply `LocalLevelConfiguration` + `DistrictConfiguration` from `Courier.Geography.Infrastructure` in its `TestDbContext`; pass a fake `IBranchLookupService`), `tests/Api.Tests/PickupsEndpointsTests.cs`

**Interfaces:**
- Consumes: `PickupStageRules.Derive`, `PickupStageRules.IsPastPickup` (Task 2); `IBranchLookupService.GetNamesByIdsAsync(IReadOnlySet<Guid>, ct)`; `PublicTrackingAreaFormatter.Format(localLevelName, districtName, city)` (existing, `PublicTrackingDtos.cs`).
- Produces:
  ```csharp
  public record PickupShipmentSummary(Guid Id, string Awb, string? DestinationArea, decimal? CodAmount); // CodAmount null when Shipment.CodAmount == 0
  public record PickupRequestResponse(Guid Id, Guid MerchantId, Guid BranchId, Guid[] ShipmentIds, DateOnly RequestedForDate,
      string? Notes, PickupRequestStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
      PickupStage Stage, string? BranchName, IReadOnlyList<PickupShipmentSummary> Shipments);
  public PickupRequestService(DbContext dbContext, ICurrentTenantProvider currentTenantProvider, IAuditLogger auditLogger,
      IBranchAuthorizationChecker branchAuthorizationChecker, IBranchLookupService branchLookupService);
  ```
  JSON: `{ ..., stage, branchName, shipments: [{ id, awb, destinationArea, codAmount }] }`.

- [ ] **Step 1: Write the failing service tests**
  - `ListAsync_ReturnsBranchNameAndShipmentsInShipmentIdsOrder` — pickup with ids `[b, a]` → `Shipments.Select(s => s.Id) == [b, a]`; `BranchName` from the fake lookup; `DestinationArea == "Pokhara Metropolitan City, Kaski"` from seeded LocalLevel/District rows; prepaid parcel (`CodAmount = 0`) → `null`, COD parcel 2500 → `2500`.
  - `ListAsync_MissingShipmentId_IsOmitted` — one id with no row → `Shipments.Count == 1`, `ShipmentIds.Length == 2`.
  - `ListAsync_ShipmentOfAnotherMerchant_IsNotReturned` — a pickup row (seeded directly) whose `ShipmentIds` contains another merchant's shipment → that parcel absent.
  - `ListAsync_StageIsDerived` — parcels `[PickedUp, Delivered]` → `Stage == Collected`.
  - `CancelAsync_ParcelPastPickup_Throws` — one parcel `PickedUp` → `ShipmentValidationException` with message `"A rider has already collected parcels from this pickup"`; stored status unchanged.
  - `CancelAsync_ParcelsCreatedOrAssigned_Succeeds` — `[Created, PickupAssigned]` → `Cancelled`, response `Stage == Cancelled`.
  - Existing tests stay green (update their `CreateService` to pass the fake lookup).
- [ ] **Step 2: Run** `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~PickupRequestServiceTests"` — FAIL.
- [ ] **Step 3: Implement**
  - One private `Task<List<PickupRequestResponse>> ToResponsesAsync(IReadOnlyList<PickupRequest> pickups, CancellationToken ct)` used by create, list and cancel: one `Shipment` query over the union of all `ShipmentIds` filtered by `(id, merchantId)` pairs (`allIds.Contains(s.Id) && merchantIds.Contains(s.MerchantId)`, then in memory keep only `s.MerchantId == pickup.MerchantId`), projecting `Id, Awb, MerchantId, CurrentStatus, CodAmount, RecipientLocalLevelId, RecipientDistrictId, RecipientCity`; one `GetNamesByIdsAsync` over distinct `BranchId`s; one `LocalLevel` and one `District` name query over the distinct ids. No per-pickup queries.
  - `Stage = PickupStageRules.Derive(p.Status, resolvedParcels.Select(s => s.CurrentStatus).ToList())`.
  - Cancel: after the existing stored-status check, load the pickup's parcel statuses (same merchant filter); if any `IsPastPickup` → throw with the exact message above.
  - Update the `PickupRequestService` registration only if DI needs it (it's `AddScoped<PickupRequestService>()`; constructor injection resolves `IBranchLookupService` already registered).
- [ ] **Step 4: API tests** in `PickupsEndpointsTests.cs`
  - `ListPickups_ReturnsBranchNameStageAndShipments` — merchant creates two shipments + pickup → item has `branchName == "Branch <code>"`, `stage == "Requested"`, `shipments[*].awb` equal to the created AWBs in order.
  - `ListPickups_SecondMerchantSameOrg_SeesNoneOfFirstMerchantsParcels` — second merchant's list contains no pickup and no AWB of the first.
  - Existing `ListPickups_BranchRestrictedStaff_OnlySeesRequestsForGrantedBranch` stays green and its item now carries `shipments`.
- [ ] **Step 5: Run** `dotnet test tests/Shipments.Tests` then `dotnet test tests/Api.Tests --filter "FullyQualifiedName~Pickups"` — PASS.
- [ ] **Step 6: Commit** `feat(shipments): pickup list names branch, lists parcels, derives stage; cancel refuses collected pickups`

---

### Task 4: Web pickups model — types, stage grouping, tiles, Kathmandu dates

**Files:**
- Modify: `src/web/src/features/merchant-pickups/types.ts`
- Create: `src/web/src/lib/dates/kathmandu.ts` (+ `kathmandu.test.ts`)
- Create: `src/web/src/features/merchant-pickups/lib/pickupView.ts` (+ `pickupView.test.ts`)
- Modify: `src/web/src/components/status-badge/statusConfig.ts` (add `PickupStage.*`)

**Interfaces:**
- Produces:
  ```ts
  // types.ts
  export type PickupStage = "Requested" | "Scheduled" | "Collected" | "Cancelled";
  export interface PickupParcel { id: string; awb: string; destinationArea: string | null; codAmount: number | null }
  export interface PickupRequest { /* existing fields */ stage: PickupStage; branchName: string | null; shipments: PickupParcel[] }

  // lib/dates/kathmandu.ts
  export function kathmanduToday(now?: Date): string;                       // "yyyy-MM-dd" in CONSOLE_TIME_ZONE
  export function daysBetween(fromIsoDate: string, toIsoDate: string): number; // calendar days, date strings only
  export function relativeDayLabel(isoDate: string, now?: Date): string;     // "Today" | "Tomorrow" | "In N days" | "Date passed"

  // features/merchant-pickups/lib/pickupView.ts
  export type PickupTab = "upcoming" | "past";
  export function isOpenStage(stage: PickupStage): boolean;                   // Requested | Scheduled
  export function splitPickups(list: PickupRequest[], now?: Date): { upcoming: PickupRequest[]; past: PickupRequest[] };
  export function pickupTiles(list: PickupRequest[], now?: Date): {
    next: { date: string; branchName: string | null; parcels: number } | null;
    waiting: number;
    collectedThisMonth: { pickups: number; parcels: number };
  };
  export function parcelsCell(p: PickupRequest): { count: number; awbs: string[]; more: number }; // first two AWBs
  ```
  `statusConfig`: `"PickupStage.Requested"` label "Requested" token `status-created`; `Scheduled` "Scheduled" `status-sealed`; `Collected` "Collected" `status-delivered`; `Cancelled` "Cancelled" `status-cancelled`.

- [ ] **Step 1: Write failing tests**
  - `kathmandu.test.ts`: `kathmanduToday(new Date("2026-10-31T23:30:00Z")) === "2026-11-01"`; `relativeDayLabel("2026-11-01", that)` "Today", `"2026-11-02"` "Tomorrow", `"2026-11-05"` "In 4 days", `"2026-10-31"` "Date passed"; `daysBetween("2026-12-31","2027-01-01") === 1`.
  - `pickupView.test.ts` (now = `2026-10-31T23:30:00Z`):
    - Upcoming = stage Requested/Scheduled sorted by date ascending, with a past-dated open pickup **first**; Past = Collected/Cancelled sorted by date descending.
    - `next` ignores open pickups dated before today and Collected/Cancelled; `null` when none.
    - `waiting` counts stage `Requested` only (a stored `Requested` with stage `Scheduled` is not counted).
    - `collectedThisMonth` counts stage `Collected` dated `2026-11-01` (yes) and `2026-10-31` (no) and sums their `shipments.length`.
    - `parcelsCell` with 5 parcels → `{ count: 5, awbs: [first two], more: 3 }`; with 0 → `{ count: 0, awbs: [], more: 0 }`.
- [ ] **Step 2: Run** `npx vitest run src/lib/dates/kathmandu.test.ts src/features/merchant-pickups/lib --maxWorkers=2` (from `src/web`) — FAIL.
- [ ] **Step 3: Implement** — `kathmanduToday` via `Intl.DateTimeFormat("en-CA", { timeZone: CONSOLE_TIME_ZONE, ... })`; `daysBetween` via `Date.UTC` on the split parts. First check `lib/dates` and `lib/i18n/bsDate.ts` (`isoDateInZone`) — reuse/export the existing zone-date helper rather than duplicating it.
- [ ] **Step 4: Run** the same command — PASS; `npx tsc --noEmit` — the page still compiles (new fields are additive).
- [ ] **Step 5: Commit** `feat(web): merchant pickup stage model, tiles and Kathmandu day labels`

---

### Task 5: Pickups page and pickup Sheet

**Files:**
- Modify: `src/web/src/features/merchant-pickups/store/merchantPickupPanelStore.ts` (+ its test): add `{ mode: "detail"; id: string }` and `openDetail(id)`; replace the "No detail mode" comment.
- Create: `src/web/src/features/merchant-pickups/components/MerchantPickupsPage.tsx` (+ `.test.tsx`)
- Create: `src/web/src/features/merchant-pickups/components/MerchantPickupDetailSheet.tsx` (+ `.test.tsx`)
- Modify: `src/web/src/app/merchant/pickups/page.tsx` → thin wrapper (`return <MerchantPickupsPage />`, like `app/merchant/cod/page.tsx`)
- Modify: `src/web/src/app/merchant/layout.tsx` (mount `<MerchantPickupDetailSheet />`)
- Modify: `src/web/src/features/merchant-pickups/hooks/useCancelMerchantPickup.ts` only if it doesn't already invalidate `["merchant-pickups"]` on settle and surface the normalized error.

**Interfaces:**
- Consumes: Task 4 types/helpers; `useMerchantPickups()`; `useCancelMerchantPickup()`; `useMerchantPermission("merchant.pickups.create" | "merchant.pickups.cancel")`; `useMerchantShipmentPanelStore().openDetail(awb)`.

- [ ] **Step 1: Write failing component tests** (mock `clientFetch` as the R5b-1 tests do)
  - Page: three tiles render values from fixture ("None booked" when no next pickup); Upcoming/Past tab triggers show counts; a past-dated open pickup row shows "Date passed"; parcels cell shows two AWBs and "+3"; no Cancel button in the table; row click calls `openDetail(id)`; list error → `ErrorState` with retry; tiles show "—" while the list errors.
  - Sheet: header "{branch} collects N parcels"; progress line marks Requested→Scheduled→Collected for the stage; Cancelled stage shows the "Cancelled" state instead; parcel table shows "Prepaid" for `codAmount === null` and `formatRs` otherwise; clicking an AWB calls the shipment store's `openDetail(awb)`.
  - Cancel: button present only when `stage === "Requested"` and `merchant.pickups.cancel`; absent for `Scheduled`; click opens AlertDialog "Cancel the pickup on {formatted date}? The branch won't send a rider."; confirm POSTs to `/api/merchant/pickups/{id}/cancel`; a 400 response shows its message inside the Sheet and the Sheet stays open.
- [ ] **Step 2: Run** `npx vitest run src/features/merchant-pickups --maxWorkers=2` — FAIL.
- [ ] **Step 3: Implement** to the `MerchantPickups` artboard: `PageHeader` "Pickups" + subtitle, **Request pickup** action when permitted (existing create Sheet); tiles via `KpiTile`; shadcn `Tabs` with tab in `?tab=past` (default upcoming, `router.replace` like `MerchantCodPage`); bordered table columns Pickup date (+ relative label), Collecting branch, Parcels, Status (`StatusBadge namespace="PickupStage"` on `stage`), Notes. Empty tab states with artboard tone. Sheet uses `SheetHeaderBlock` and the shadcn `AlertDialog`.
- [ ] **Step 4: Run** the suite — PASS; `npx tsc --noEmit` — 0 errors.
- [ ] **Step 5: Commit** `feat(web): merchant pickups page and pickup Sheet with confirmed cancel`

---

### Task 6: KYC page

**Files:**
- Modify: `src/web/src/features/merchant-kyc/types.ts` (drop `reviewedByUserId`; add `uploadedAt: string | null`, `reviewedAt: string | null`, `rejectionReason: string | null`)
- Create: `src/web/src/features/merchant-kyc/lib/kycView.ts` (+ `kycView.test.ts`)
- Modify: `src/web/src/features/merchant-kyc/store/merchantKycPanelStore.ts` (+ test): `{ mode: "upload"; documentType: KycDocumentType }`, `openUpload(documentType?: KycDocumentType)` defaulting to `"CitizenshipDocument"`
- Modify: `src/web/src/features/merchant-kyc/components/MerchantKycUploadSheet.tsx` (initialise/reset the Select from `panel.documentType` on open; restyle header with `SheetHeaderBlock`)
- Create: `src/web/src/features/merchant-kyc/components/MerchantKycPage.tsx` (+ `.test.tsx`); `app/merchant/kyc/page.tsx` → thin wrapper

**Interfaces:**
- Produces:
  ```ts
  export const REQUIRED_KYC_TYPES: readonly KycDocumentType[]; // Citizenship, PanCard, BusinessRegistrationCertificate, BankAccountProof
  export const KYC_TYPE_LABEL: Record<KycDocumentType, string>; // "Citizenship document", "PAN card", "Business registration certificate", "Bank account proof", "Other"
  export type KycRowState = "Approved" | "InReview" | "Rejected" | "NotUploaded";
  export interface KycRow { type: KycDocumentType; state: KycRowState; doc: MerchantKycDocument | null }
  export function latestPerType(docs: MerchantKycDocument[]): Map<KycDocumentType, MerchantKycDocument>; // max uploadedAt; null uploadedAt sorts last; tie → later list position
  export function requiredRows(docs: MerchantKycDocument[]): KycRow[];          // 4 rows, REQUIRED_KYC_TYPES order
  export function otherDocs(docs: MerchantKycDocument[]): MerchantKycDocument[]; // all "Other", newest first
  export function kycProgress(rows: KycRow[]): { approved: number; rejected: number; inReview: number; total: 4 };
  ```

- [ ] **Step 1: Write failing tests**
  - `kycView.test.ts`: legacy (null `uploadedAt`) Rejected + newer Pending of the same type → the Pending wins; two nulls → later in list wins; a missing type → `NotUploaded`; `Pending` → `InReview`; progress `{ approved: 2, rejected: 1, inReview: 1 }` for a mixed fixture.
  - `MerchantKycPage.test.tsx`: four required rows in order with state chips; Rejected row shows "Courier note: {reason}" and an **Upload again** button that calls `openUpload("PanCard")`; NotUploaded row shows **Upload**; meta line "Uploaded {formatDate} · rejected {formatDate}" and "Uploaded —" for null; progress text "2 of 4 documents approved"; "Upload other document" calls `openUpload("Other")`; error → `ErrorState`.
  - Store test: `openUpload()` → `documentType: "CitizenshipDocument"`; `openUpload("BankAccountProof")` keeps it.
- [ ] **Step 2: Run** `npx vitest run src/features/merchant-kyc --maxWorkers=2` — FAIL.
- [ ] **Step 3: Implement** to the `MerchantKyc` artboard (`PageHeader` "KYC verification", progress band with a four-segment bar, rows inside `SectionCard`, rejection reason in `text-destructive`). Fix any other importer of `reviewedByUserId` (`tsc` will list them).
- [ ] **Step 4: Run** the suite — PASS; `npx tsc --noEmit` — 0 errors.
- [ ] **Step 5: Commit** `feat(web): merchant KYC page with per-document status, rejection reason and preselected upload`

---

### Task 7: Staff page

**Files:**
- Create: `src/web/src/features/merchant-staff/lib/staffView.ts` (+ test), `src/web/src/features/merchant-staff/components/MerchantStaffPage.tsx` (+ `.test.tsx`); `app/merchant/staff/page.tsx` → thin wrapper

**Interfaces:**
- Produces: `export function roleCounts(staff: MerchantStaffMember[]): { Owner: number; Staff: number }` (counts all rows, active or not); `export function initials(fullName: string): string` (first letters of the first and last words, uppercase; one word → one letter; blank → "?").

- [ ] **Step 1: Write failing tests**
  - `roleCounts` over a fixture with 1 owner, 2 active staff, 1 inactive staff → `{ Owner: 1, Staff: 3 }`; `initials("Sita Kumari Rai") === "SR"`, `initials("Ram") === "R"`, `initials("  ") === "?"`.
  - Page: without `merchant.staff.manage` → the existing "You don't have access to this page" state and **no** fetch issued (assert `clientFetch` not called); with it → subtitle contains `merchantDisplayName` from `useMerchantSession`; two role cards with counts and one-line descriptions; table rows with initials avatar, name, email, role, `Active`/`Inactive` chip; inactive row has the muted class; no row action buttons; **Invite staff** opens the existing Sheet.
- [ ] **Step 2: Run** `npx vitest run src/features/merchant-staff --maxWorkers=2` — FAIL.
- [ ] **Step 3: Implement** to the `MerchantStaff` artboard, keeping the owner-only guard first (before the query hook mounts — split into an inner component as the current page does).
- [ ] **Step 4: Run** — PASS; `npx tsc --noEmit` — 0 errors.
- [ ] **Step 5: Commit** `feat(web): merchant staff page with role cards`

---

### Task 8: API keys page and confirmed revoke

**Files:**
- Create: `src/web/src/features/merchant-api-keys/lib/scopeLabels.ts` (+ test), `lib/apiKeyView.ts` (+ test)
- Modify: `src/web/src/features/merchant-api-keys/components/MerchantApiKeyCreateSheet.tsx` (import `AVAILABLE_SCOPES` + labels from `scopeLabels.ts`; checkbox label = plain-language label with the raw scope underneath in mono)
- Create: `src/web/src/features/merchant-api-keys/components/RevokeApiKeyDialog.tsx`, `MerchantApiKeysPage.tsx` (+ `.test.tsx`); `app/merchant/api-keys/page.tsx` → thin wrapper

**Interfaces:**
- Produces:
  ```ts
  export const AVAILABLE_SCOPES: readonly string[];          // moved verbatim from the create Sheet (same 15 entries, same order)
  export const SCOPE_LABELS: Record<string, string>;         // one entry per AVAILABLE_SCOPES item, e.g. "shipment.create" → "Book shipments", "merchant.cod.read" → "View COD"
  export function scopeLabel(scope: string): string;         // unknown → the raw scope
  export function splitKeys(keys: ApiKey[]): { active: ApiKey[]; revoked: ApiKey[] }; // isActive; each newest createdAt first
  export function isRecentlyUsed(lastUsedAt: string | null, now?: Date): boolean;      // within 24 h
  export function lastUsedLabel(lastUsedAt: string | null, now?: Date): string;        // "Never" | "12 min ago" | "3 h ago" (< 24 h) | formatDate
  ```

- [ ] **Step 1: Write failing tests**
  - `scopeLabels.test.ts`: every `AVAILABLE_SCOPES` entry has a non-empty label distinct from the raw scope; `scopeLabel("x.unknown") === "x.unknown"`; `AVAILABLE_SCOPES` does not contain `"merchant.apikeys.manage"`.
  - `apiKeyView.test.ts`: `lastUsedLabel(null)` "Never"; 12 min ago "12 min ago"; 3 h ago "3 h ago"; 2 days ago → `formatDate` output; `isRecentlyUsed` true at 23 h, false at 25 h and null; `splitKeys` ordering.
  - Page: owner gate holds (no `merchant.apikeys.manage` → access-denied state, no fetch); Active/Revoked tabs with counts; scope chips show labels; revoked row shows "Revoked {date}" and no Revoke button; **Revoke** opens dialog titled "Revoke key {prefix}…?" with the cannot-be-undone copy; the "It was last used {label}, so something is probably still calling it." line appears only when used within 24 h; **Keep key** closes without a request; **Revoke key** calls the revoke endpoint.
- [ ] **Step 2: Run** `npx vitest run src/features/merchant-api-keys --maxWorkers=2` — FAIL.
- [ ] **Step 3: Implement** to the `MerchantApiKeys` artboard (prefix in `font-mono` with "…" suffix, scope chips via `Badge variant="outline"`, destructive `AlertDialogAction`).
- [ ] **Step 4: Run** — PASS; `npx tsc --noEmit` — 0 errors.
- [ ] **Step 5: Commit** `feat(web): merchant API keys page with scope labels and confirmed revoke`

---

### Task 9: Docs, full verification

**Files:**
- Modify: `docs/API-SPEC.md` — `GET /pickups` response fields (`stage`, `branchName`, `shipments[]`; `status` kept for compatibility; cancel 400 when collected); `POST /merchants/{id}/kyc-documents/{docId}/review` body `{ status, rejectionReason }` (required on Rejected, ≤500, **shown to the merchant**); merchant KYC list fields (no reviewer id).
- Modify: `docs/FRONTEND-ROADMAP.md` — R5b-2 row → Done with the spec path; follow-ups: staff KYC review screen, staff deactivate/reactivate, KYC file view.

- [ ] **Step 1: Edit both docs.**
- [ ] **Step 2: Verify backend** — `dotnet build` 0 errors; `dotnet test tests/Merchants.Tests`, `tests/Shipments.Tests`, then `tests/Api.Tests --filter "FullyQualifiedName~Pickups|FullyQualifiedName~MerchantKyc"` — all PASS (run one at a time).
- [ ] **Step 3: Verify web** — from `src/web`: `npx tsc --noEmit` 0 errors; `npx vitest run --maxWorkers=2` green (rerun any 5 s-timeout file alone); `npm run lint` clean.
- [ ] **Step 4: Commit** `docs: R5b-2 pickup/KYC API changes and roadmap`
