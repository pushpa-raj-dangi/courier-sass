# F15 R5b-2 — Merchant portal: account and setup pages

Date: 2026-11-17 · Status: draft for approval

## Intent

R5b-1 rebuilt the merchant portal's daily pages (dashboard, shipments, booking, COD). The four
setup pages, **Pickups, KYC, Staff and API keys**, are still R1-era tables with an `h1` and a
button. This phase brings them in line with the console look (page primitives, status chips,
detail Sheets). It also fills the data gaps that make them hard to use today:

- Pickups show "3 shipments" without saying which parcels or which branch is collecting them.
- KYC shows "Rejected" with no reason and no dates, so a merchant can't fix a rejected document.
- API keys show "4 scopes" and revoke with one click, which breaks a live integration
  instantly with no warning.

Artboards: canvas https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu, row "Merchant portal:
account and setup" (`MerchantPickups`, `MerchantKyc`, `MerchantStaff`, `MerchantApiKeys`).

## Goals

1. Each page uses the console page primitives (`PageHeader`, `SectionCard`/`KpiTile`, the
   bordered table, `StatusBadge`, `SheetHeaderBlock`) and matches the artboards.
2. Pickups name the collecting branch and list the actual parcels.
3. A rejected KYC document tells the merchant why, and when it was uploaded and reviewed.
4. Destructive merchant actions (cancel pickup, revoke key) are confirmed first.

## Non-goals

- Staff deactivate, reactivate, remove, role change, or resend invite (no new staff writes).
- A staff-side KYC review screen. The staff web app has none today; reviews go through
  `POST /merchants/{id}/kyc-documents/{docId}/review`. Building that screen is a follow-up.
- Viewing or downloading a KYC file from the merchant portal.
- Editing or rescheduling a pickup; adding parcels to an existing pickup.
- API key rotation, expiry, or per-key usage stats beyond `lastUsedAt`.
- Changes to the create/upload/invite Sheets beyond the restyle (they work today).

## Anti-goals

- No staff-internal data on merchant pages: no rider names, staff reviewer names, or internal
  branch notes. The KYC rejection reason is written *for the merchant*.
- No data from another merchant, even in the same organization.
- No "fake" numbers: tiles and counts are derived from real API data or not shown.

## Approach

Picked by the user: **B — restyle plus small backend additions**, matching R5b-1's shape.
The alternatives were A (frontend-only restyle; KYC still couldn't say why a document was
rejected) and C (B plus staff deactivate/reactivate; deferred).

The user also approved one write-side change: KYC review now records a rejection reason
(required when rejecting) and review/upload timestamps.

## Backend changes

### 1. KYC document: dates and rejection reason (Merchants module)

- `MerchantKycDocument` gains `UploadedAt` (`DateTime?`), `ReviewedAt` (`DateTime?`) and
  `RejectionReason` (`string?`, max 500).
- EF migration. Existing rows keep `UploadedAt = null`. Nothing reliable exists to backfill
  from, and the UI shows "Uploaded —" for those rows.
- Upload sets `UploadedAt = now` (UTC).
- `ReviewKycDocumentRequest` becomes `(KycStatus Status, string? RejectionReason)`:
  - `Rejected` requires a non-blank reason (trimmed, ≤500), else 400 `MERCHANT_VALIDATION_FAILED`.
  - `Approved` clears any stored reason; a reason sent with Approved is ignored.
  - Sets `ReviewedAt = now`.
  - The audit entry gains before/after status and the reason, per CLAUDE.md §28.
- `MerchantKycDocumentResponse` gains `UploadedAt`, `ReviewedAt`, `RejectionReason`.
  - The merchant-facing list (`GET /merchant/kyc-documents`) keeps hiding reviewer identity.
  - Drop `ReviewedByUserId` from the *merchant* response (staff response keeps it). It is a
    staff user id and leaks staff-internal data. Implemented as a separate merchant DTO, or by
    mapping, at the builder's discretion.

### 2. Pickup list: branch name and parcels (Shipments module)

`GET /pickups` (both merchant and staff callers) adds to each `PickupRequestResponse`:

- `BranchName` (string), from `IBranchLookupService`.
- `Shipments`: `[{ id, awb, destinationArea, codAmount }]`, in `ShipmentIds` order.
  `destinationArea` uses the same "LocalLevel, District" / city fallback as R5a's public
  tracking `DestinationArea` (reuse that helper; don't duplicate it). `codAmount` is null for
  prepaid.

Rules:

- Batched lookups (one shipment query and one branch lookup per list call, not N+1).
- Shipments are filtered by the caller's tenant (and merchant, for merchant callers) as
  today's list already does. A shipment id that doesn't resolve is omitted, not an error.
- `ShipmentIds` stays in the response for compatibility.

### 3. Pickup stage derived from its parcels (Shipments module)

Nothing in the backend moves a `PickupRequest` to `Scheduled` or `Completed`; only `Cancelled`
is ever written. Picked by the user: work out the stage from the parcels at read time, with no
new staff step and no new stored state.

- A pure domain function, `PickupStage Derive(PickupRequestStatus stored,
  IReadOnlyCollection<ShipmentStatus> parcels)`, returns `Requested | Scheduled | Collected |
  Cancelled`:
  - Stored `Cancelled` → `Cancelled`.
  - Stored `Completed` → `Collected` (forward-compatible, in case something sets it later).
  - The parcels used are those not `Cancelled`. If there are none, the stage is `Requested`.
  - `Collected` when every parcel used is past pickup, meaning any status other than
    `Created` or `PickupAssigned`. Physically picked up is what counts, including RTO and
    international states.
  - Otherwise `Scheduled` when at least one parcel is `PickupAssigned` or already past pickup.
  - Otherwise `Requested`.
- `PickupRequestResponse` gains `Stage` (`PickupStage`). The stored `Status` stays in the
  response for compatibility. The web UI reads `Stage` only.
- Unit-tested exhaustively over the status combinations above.

### 4. Cancel guard uses the parcels too

`PickupRequestService.CancelAsync` also refuses (400 `SHIPMENT_VALIDATION_FAILED`, "A rider has
already collected parcels from this pickup") when any parcel in the pickup is past pickup. Today
a merchant can cancel a pickup the rider already completed. The web UI shows **Cancel pickup**
only while `Stage == Requested`. That is stricter than the server, because a merchant shouldn't
cancel after the branch assigned a rider. The server also allows `Scheduled`, since
reassigning stays the branch's call.

No other endpoints change. Staff, API keys and pickup create stay as they are.

### 5. Calls made after the builder check (Claude's calls)

- **Re-review allowed:** staff may review a document that was already reviewed (e.g. approve a
  previously rejected one), as today. The audit entry records before/after status and reason.
- **Staff responses also carry the new KYC fields** (`UploadedAt`, `ReviewedAt`,
  `RejectionReason`) alongside `ReviewedByUserId`. Only the merchant response drops the
  reviewer id.
- **Legacy KYC rows with a null `UploadedAt`** sort last in the latest-per-type rule.

## Frontend changes (`src/web`, merchant features)

All four pages keep their current permission gates (owner-only for Staff and API keys,
`merchant.pickups.*` for pickup actions) and loading/error/empty states, restyled with the
shared state components.

### Pickups (`/merchant/pickups`, artboard `MerchantPickups`)

- `PageHeader` "Pickups" + subtitle; **Request pickup** button (existing create Sheet) when
  permitted.
- Three tiles, all computed client-side from the list:
  - **Next pickup:** the earliest upcoming pickup's date, branch and parcel count, or "None
    booked".
  - **Waiting to be scheduled:** count of stage `Requested`.
  - **Collected this month:** count of stage `Collected` whose `requestedForDate` falls in the
    current month (org calendar, as R5b-1's `OrgCalendar` rule), plus total parcels.
  - All three tiles use `stage`, not the stored status.
- Tabs: **Upcoming** (stage `Requested` or `Scheduled`; soonest first) and **Past** (stage
  `Collected` or `Cancelled`; latest first), each with a count.
  - An open pickup whose date has already passed (org calendar) stays in Upcoming. It sorts
    to the top with a "Date passed" label in place of the relative label.
  - The **Next pickup** tile only considers open pickups dated today or later.
- Table columns:
  - Pickup date, with a relative label (Today / Tomorrow / In N days).
  - Collecting branch.
  - Parcels: the count plus the first two AWBs, then "+N".
  - Status.
  - Notes.
- Row click opens a **pickup Sheet** (Zustand panel store, per the Sheet-over-pages rule):
  - Header: date, status, "{branch} collects N parcels".
  - Progress line: Requested → Scheduled → Collected. A cancelled pickup shows a "Cancelled"
    state instead.
  - The merchant's note.
  - Parcel table (AWB, destination area, COD or "Prepaid"). An AWB opens the existing
    merchant shipment detail Sheet.
  - Footer **Cancel pickup**: only while `stage == Requested` and permitted. It asks for confirmation
    (AlertDialog: "Cancel the pickup on {date}? The branch won't send a rider.") before
    calling the existing cancel endpoint.
- The table's inline Cancel button goes away; cancel lives in the Sheet.

### KYC (`/merchant/kyc`, artboard `MerchantKyc`)

- `PageHeader` "KYC verification" + subtitle.
- The required set is a frontend constant: Citizenship document, PAN card, Business
  registration certificate, Bank account proof. `Other` is optional.
- Each required type is one row. The row reflects that type's *latest* upload (by
  `uploadedAt`, then list order):
  - Status: Approved, In review (Pending), Rejected, or Not uploaded.
  - Meta line: "Uploaded {d} · approved/rejected {d}".
  - For Rejected: the reason in the destructive colour, prefixed "Courier note:".
  - Action: **Upload** (not uploaded) or **Upload again** (rejected). It opens the existing
    upload Sheet with the document type preselected.
- Progress band: "{approved} of 4 documents approved", a sub-line counting rejected/in-review,
  and a four-segment bar.
- Footer row: "Upload other document" opens the upload Sheet with `Other`. Uploaded `Other`
  documents list below the required rows the same way.

### Staff (`/merchant/staff`, artboard `MerchantStaff`)

- `PageHeader` "Staff" + subtitle naming the merchant; **Invite staff** (existing Sheet).
- Two role cards (Owner, Staff), each with a count from the list and a one-line description
  of what that role can do.
- Table: avatar initials + name, email, role, status chip (Active / Inactive). Inactive rows
  are muted. No row actions.

### API keys (`/merchant/api-keys`, artboard `MerchantApiKeys`)

- `PageHeader` "API keys" + subtitle; **Create API key** (existing Sheet).
- Tabs: **Active** and **Revoked**, with counts.
- Table: key prefix (mono, "…" suffix), scopes as plain-language chips, created date, last
  used ("12 min ago" relative for the last 24 h, else a date; "Never"). Revoked rows show the
  revoked date instead of the Revoke action.
- Scope labels come from one shared map, also used by the create Sheet's checkboxes (which show
  the label with the raw scope underneath). An unknown scope falls back to the raw string.
- **Revoke** opens an AlertDialog: "Revoke key {prefix}…?", saying anything using it stops
  working right away and this can't be undone. If `lastUsedAt` is within 24 h it adds "It was
  last used {relative}, so something is probably still calling it." Buttons: Keep key / Revoke
  key (destructive).

## Security and authorization

- No new routes. Existing permission filters stay (`PickupsRead`/`pickup.read`,
  `merchant.kyc.review`, `merchant.staff.manage`, `merchant.apikeys.manage`).
- Pickup enrichment must not widen visibility. Shipments are joined only within the caller's
  existing scope, and a merchant never receives another merchant's AWBs.
- The merchant KYC response drops `ReviewedByUserId`.
- The rejection reason is merchant-visible by design. The API doc says so, so staff write it
  for the merchant.

## Tests

- **Merchants:**
  - Reject without a reason → 400.
  - Reject with a reason → reason, `ReviewedAt` and audit stored.
  - Approve clears the reason.
  - Upload sets `UploadedAt`.
  - The merchant list returns the new fields and no `ReviewedByUserId`.
  - Cross-org document → 404 (existing test stays green).
- **Shipments/API:**
  - The pickup list returns `BranchName` and `Shipments` in order.
  - A merchant sees only its own parcels.
  - A second merchant in the same org sees none of the first's parcels.
  - A staff caller still gets its branch-scoped list.
  - A missing shipment id is omitted.
  - `PickupStage.Derive` unit tests over every status combination.
  - `Stage` is present in the list.
  - Cancel is refused once any parcel is past pickup, and still works while all parcels are
    `Created` or `PickupAssigned`.
- **Web (vitest):**
  - Pickup tab split and sort; the tile derivations, including the month boundary.
  - The pickup Sheet: cancel only while Requested, behind the confirm dialog.
  - KYC latest-per-type selection, the four row states, and the progress count.
  - The API key scope label map and fallback; the revoke dialog's recent-use line; the
    Active/Revoked split.
  - Staff role counts.
  - The owner-only gates still hold for Staff and API keys.

## Docs

- Update `docs/API-SPEC.md` (pickup response fields, KYC review request/response).
- Mark R5b-2 Done in `docs/FRONTEND-ROADMAP.md` and log the follow-ups:
  - A staff KYC review screen.
  - Staff deactivate.
  - KYC file view.

## Left to the builder

- Component and file layout inside the existing `merchant-*` feature folders.
- Whether the merchant KYC DTO is a new record or a mapping.
- Relative-date helper reuse (check existing `lib` date utilities before adding one).
- Exact copy for empty states, within the artboards' tone.
