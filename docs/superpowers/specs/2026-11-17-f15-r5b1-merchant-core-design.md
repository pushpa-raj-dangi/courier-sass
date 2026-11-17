# F15 R5b-1 — Merchant portal core (dashboard, shipments, booking, COD)

Date: 2026-11-17. Design source: F15 canvas (https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu),
"Merchant portal" row — `MerchantDashboard`, `MerchantShipments` (list + detail Sheet),
`MerchantBooking` (book Sheet), `MerchantCod` (COD + settlement Sheet), `MerchantSidebar`.
R5 was split: R5a public tracking (done, `062b474`), R5b-1 this doc, R5b-2 the setup pages
(pickups, KYC, staff, API keys — restyle with existing page primitives, separate cycle).

## Intent

Merchants (Nepal online sellers) open the portal for three jobs, in this order:

1. **Is my money coming?** COD is their cash flow — what is collected, what is in a settlement,
   what has been paid.
2. **What needs my attention?** Failed deliveries and returns cost them money.
3. **Send more parcels** — book shipments and request pickups fast.

The pages that serve those jobs get the F15 look and honest, server-computed numbers. Today the
dashboard counts the first 100 shipments in the browser and the list is a plain table.

## Goals

- Dashboard, Shipments (+ detail Sheet), Book shipment Sheet and COD (+ settlement Sheet) match the
  artboards.
- Every number is exact and server-backed (no client aggregation over a truncated page).
- Merchants only ever see their own data; nothing staff-internal (staff/rider names, internal
  notes, custody user ids) appears.

## Non-goals

Pickups, KYC, staff, API keys pages (R5b-2). Bulk CSV upload, label printing, reschedule/NDR
actions by merchants, notifications feed. Changing booking rules, pricing or the COD lifecycle.

## Anti-goals

- A number on screen that the backend can't back (use the existing `ComingSoonSlot` only if a
  value is truly unavailable — this design adds the reads so none should be needed).
- Merchant pages showing staff names, rider names, internal branch notes or another merchant's data.
- Business logic in React components (CLAUDE.md §3) — grouping/aggregation lives in the backend or
  in small pure `lib/` helpers with tests.

## Constraints

- Frontend rules from memory/DESIGN.md: shadcn/ui components and blocks (Tabs, Pagination,
  DatePicker, Checkbox, Spinner, Sheet…), detail/create views are right-side Sheets driven by a
  Zustand store (never routed pages), on-scale font sizes only (`text-2xs`…), console tokens.
- Merchant auth: merchant JWT via the BFF (`merchantApiFetch`), permissions from
  `MerchantPermissionCatalog`; nav items stay permission-gated (`merchantNav.ts`).
- Tenant + merchant isolation server-side: every new read self-scopes to `User.GetMerchantId()`;
  a staff token gets 403 on merchant-only routes.
- No new library. No migration expected (all reads over existing tables).

## Approaches (decided)

- A: draw all 7 artboards, one branch — most drawing for low-value setup pages.
- B: artboards for the 3 core pages, restyle setup pages, one branch.
- **C (picked by the user): as B but two branches** — R5b-1 core + backend reads (this doc), R5b-2
  setup pages. Smaller reviews.

## Status groups (merchant vocabulary)

One shared mapping, used by tabs, dashboard counts and badges. Lives in a pure web helper
(`features/merchant-shipments/lib/statusGroups.ts`) as lists of internal `ShipmentStatus` names,
sent to the existing list filter `GET /api/v1/shipments?status=…` (repeatable).

| Group | Label | Internal statuses |
|---|---|---|
| booked | Booked | Created, PickupAssigned |
| inTransit | In transit | PickedUp, AtOriginBranch, Sorted, InTransit, AtDestinationBranch, CustomsPrepared, HandedToCarrier, InInternationalTransit, CustomsHold, CustomsClearance, ReceivedFromCarrier |
| outForDelivery | Out for delivery | OutForDelivery |
| needsAttention | Needs attention | DeliveryFailed, NdrCreated, InternationalException |
| returning | Returning | RtoInitiated, RtoInTransit, RtoAtOrigin, RtoDelivered |
| delivered | Delivered | Delivered, DeliveredAbroad |

`Cancelled` appears only under "All". Merchant-facing status labels on rows/badges: Booked,
Picked up, In transit, At destination branch, Out for delivery, Delivery failed, Returning to you,
Returned to you (RtoDelivered), Delivered, Cancelled (builder: extend the existing merchant/Public
label set in `statusConfig.ts` rather than inventing a parallel map).

## Backend (Cod + Shipments modules, read-only, additive)

All three routes: merchant JWT or merchant API key, `RequireMerchantPermissionAnyCredential(
MerchantPermissionCatalog.CodSettlementsRead)`, self-scoped to the caller's merchant, staff → 403,
other merchant's settlement id → 404. Registered on the existing merchant COD group pattern in
`CodEndpoints.cs`.

1. **`GET /api/v1/cod/merchant-summary`** →
   ```
   { collectedNotSettled: { amount, parcels },   // collections Status in CodCollected..FinanceVerified
                                                 // with MerchantSettlementItemId == null; amount = Σ CollectedAmount
     inSettlement:        { amount, settlements },// MerchantSettlement Status Created; Σ TotalPayable
     settledThisMonth:    { amount, payouts, lastSettledAt }, // Settled with SettledAt in current month
     deliveredToday:      { parcels, codCollected } }          // collections CollectedAt in today
   ```
   "Today" and "this month" are **Nepal time (Asia/Kathmandu, UTC+05:45)** — this is a Nepal
   domestic product; earlier phases logged UTC "today" as a bug. Helper computes the UTC range.
   `deliveredToday.parcels` = delivered shipments of this merchant whose Delivered event is today
   (builder: via an existing Shipments lookup service or a new small one; COD-only would undercount
   prepaid deliveries).
2. **`GET /api/v1/cod/merchant-awaiting?page&pageSize`** → `PagedResult` of the merchant's
   collections not yet in a settlement (same predicate as `collectedNotSettled`), newest
   `CollectedAt` first: `{ shipmentId, awb, collectedAt, collectedAmount, courierFee
   (= ShipmentTotalCharge), net (= collected − fee) }`. AWB via the existing
   `IShipmentAwbLookupService`.
3. **`GET /api/v1/cod/settlements/{id}/export`** → `text/csv`, `Content-Disposition` attachment
   `settlement-{yyyyMMdd from}-{yyyyMMdd to}.csv`; header `AWB,COD amount,Courier fee,Other
   deductions,Net payable`, one row per `MerchantSettlementItem`, then a `Total` row. Same CSV
   streaming pattern as `ReportsEndpoints` (escape fields).

4. **`GET /api/v1/cod/merchant-shipments/{shipmentId}/cod-status`** → the detail Sheet's COD line:
   `{ state: "Prepaid" | "NotCollected" | "CollectedAwaitingSettlement" | "InSettlement" | "Settled",
   settlementId?, periodFrom?, periodTo?, settledAt? }`. Prepaid = no collection row / zero COD;
   NotCollected = `CodExpected`; CollectedAwaitingSettlement = collected and no settlement item;
   InSettlement / Settled from the linked settlement's status. A shipment of another merchant →
   404. (Added after the builder check: the paged awaiting list can't answer this per shipment.)

**Time zone:** "today"/"this month" use the organization's `OrganizationSettings.TimeZoneId`
(defaults to `Asia/Kathmandu`), converted with `TimeZoneInfo`, never UTC dates.
**Delivered today** counts shipments of this merchant with a `Delivered` or `DeliveredAbroad`
event timestamped within today (both are terminal, so later state doesn't matter); the builder
confirms which existing Shipments lookup service can answer it or adds a small one behind an
interface the Cod module consumes (no direct cross-module DbSet use beyond existing precedent).

No audit entries (reads). Tests: Cod.Tests for the predicates and Nepal-time boundaries (a
collection at 23:50 NPT vs 00:10 NPT); Api.Tests for each route — merchant sees own numbers,
second merchant in same org sees none of the first's, staff token 403, foreign settlement id 404,
CSV content/headers.

## Frontend

BFF routes under `src/web/src/app/api/merchant/cod/…` forwarding to the three reads (CSV route
streams the body and passes content headers through).

### Dashboard (`/merchant/dashboard`)
- Header: greeting with the user's first name, merchant name, "N parcels on the way" (= sum of
  booked + inTransit + outForDelivery counts), actions **Request pickup** (opens existing pickup
  create Sheet) and **Book shipment** (opens the booking Sheet). Each action hidden without its
  permission.
- Money band (summary endpoint): Collected, not yet settled (highlighted) · In settlement ·
  Settled this month. Hidden without `merchant.cod.read`.
- "Your parcels right now": four tiles — Booked, In transit, Out for delivery (counts =
  `totalCount` of the filtered list with `pageSize=1`), Delivered today (summary endpoint, with its
  COD collected). Each tile links to `/merchant/shipments?tab=…`.
- "Needs your attention": failed deliveries (needsAttention count) and returning (returning count),
  each linking to its tab; hidden entirely when both are 0 (replace with a quiet "Nothing needs
  you right now").
- Recent shipments: latest 7, row opens the detail Sheet.

### Shipments (`/merchant/shipments`)
- Header with total booked count, same two actions.
- URL-state tabs (`?tab=`): All · Booked · In transit · Out for delivery · Needs attention (red
  count) · Returning · Delivered, each with its count.
- Filters (URL state): search `q` (AWB, recipient, phone — existing filter), booked date range
  (`createdFrom/createdTo`, shadcn DatePicker), service type. Server pagination (shadcn Pagination).
- Columns: AWB + booked time · Recipient + address line · Destination (destination branch name
  from `/api/merchant/branches`) · Service · COD ("Prepaid" when 0) · Charge (`totalCharge`) ·
  Status · Updated (`lastEventAt`, falling back to `createdAt`). Row click opens the Sheet.
- Sidebar: the Shipments nav item shows the needsAttention count as a badge (ConsoleSidebar already
  renders `item.badge`); hidden at 0.

### Shipment detail Sheet (existing `MerchantShipmentDetailSheet`, rebuilt)
- Header: eyebrow `AWB · {SERVICE} · COD|PREPAID`, AWB, status badge; facts: COD to collect, Your
  charge, You receive (COD − charge; hidden for prepaid), Weight.
- "Where it is now": Status phrase (merchant label), Delivering branch (destination branch name),
  Last update, COD settlement — Not yet collected / Collected, awaiting settlement / In settlement
  (period) / Settled on {date}, derived from the merchant's settlements (items carry `shipmentId`)
  and the awaiting list; non-COD shows "Prepaid".
- Stepper + Journey tab from the **public tracking read** (`/api/track/{awb}`) reusing R5a's
  `buildJourney` — merchants see the same customer-safe history (no staff/rider names), per the
  existing rule that `/shipments/{awb}/events` is staff-only.
- Tabs: Journey · Parcel (weight, dimensions, items, declared value) · Recipient (name, phone,
  address) · Charges (delivery, insurance, other, total — booking-time prices).
- Footer: "Cancel shipment" only while Booked group and with `shipment.cancel` (existing cancel
  hook, confirm in an AlertDialog); "Copy tracking link" (copies `{origin}/track/{awb}`).

### Book shipment Sheet (existing `MerchantShipmentCreateSheet`, restructured)
- Three numbered sections per artboard: 01 Recipient (name, phone, province → district →
  municipality → ward, address), 02 Parcel (weight, optional dimensions, items table with add/remove;
  declared value derived from items as today), 03 Service and payment (service types as radio
  cards, pickup/deliver-from branch selects, COD amount, insurance checkbox, sender summary line
  with "change" revealing the sender fields).
- Sticky footer: live quote from the existing quote hook — Delivery · Insurance · Your charge ·
  You receive — and primary button "Book for Rs {total}" (disabled until a quote exists).
- Validation, idempotency key and submit behaviour unchanged.

### COD (`/merchant/cod`)
- Three-step strip from the summary endpoint (same numbers as the dashboard band).
- URL-state tabs: **Settlements** (existing list; columns Period · Parcels · COD collected · Fees ·
  You receive · Status (Awaiting payout / Settled) · Paid on; period filter Last 30 / 90 days / All
  applied client-side over the existing unpaginated list) and **Awaiting settlement** (new
  paged awaiting read: AWB · Collected on · COD · Fee · Net).
- Settlement Sheet (existing `MerchantSettlementDetailSheet`, rebuilt): eyebrow with period, total,
  status, parcel count; "how it adds up" (Σ COD, − courier fees, − other deductions, = you receive —
  computed from items); items table; footer "Download CSV" (export route).

### States
Every section has loading skeletons, an error state with retry, and an empty state ("No shipments
yet — book your first one", "No settlements yet", etc.). Counts that fail show "—", never 0.

## Builder's calls (Claude's, correctable)
- Nepal time for "today"/"this month" in the new reads.
- Merchant journey = public tracking data (consistent with existing staff-only events rule).
- Tab counts via one `pageSize=1` list request per group (6 small requests, cached by TanStack
  Query) rather than a new counts endpoint — keeps backend scope to the COD reads.
- Settlement period filter client-side (the merchant list is unpaginated today).
- Shipment JSON carries `currentStatus` as the enum's integer; rows/badges map it with the same
  numeric→name mapping `statusConfig.ts` already uses. Filter requests send enum *names*.
- Header "N shipments booked" = the All count (includes Cancelled); "N parcels on the way"
  = booked + inTransit + outForDelivery (excludes Cancelled).
- Sidebar badge: `useMerchantNavGroups` gains one needsAttention count query, only when the user has
  `shipment.read`; badge hidden at 0 or on error.
- Destination branch name: the merchant branch list returns active branches only; an unknown or
  inactive branch id renders "—".
- COD settlement line in the detail Sheet comes from the per-shipment `cod-status` read (route 4).

## Left to the builder
Exact component split inside each feature folder, query-key names, skeleton shapes, copy for empty
states beyond the examples above.
