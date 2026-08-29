# Frontend Phase F3 — Shipments — Design Spec

**Date:** 2026-08-29
**Status:** Approved for implementation planning
**Scope:** `docs/FRONTEND-ROADMAP.md` Phase F3 — shipment booking, shipment list, shipment detail.

## 1. Context

Phases F0–F2 shipped the design system, app shell, auth, the organization/branch dashboards, and
Branches (list + Sheet-based create/detail, per `docs/ARCHITECTURE.md` §9a — the standing
convention this phase follows exactly, not routed pages). This phase adds the `Shipments` feature
area the sidebar has linked to since F0 (currently a 404).

Backend work is paused for now (user's explicit instruction), so this phase works strictly within
the existing `Shipments`/`Pricing`/`Merchants` API surface. Two real gaps surfaced researching that
surface, both resolved with the user before design:

- **No staff-facing endpoint returns a shipment's `ShipmentEvent` history.** `ShipmentResponse`
  only exposes `CurrentStatus`; the only reader of `ShipmentEvent` rows is the public
  `/track/{awb}` endpoint, deliberately limited per CLAUDE.md §24 (excludes internal branch/user/
  notes data) — the wrong shape for a staff ops view. **Resolved: ship without a tracking timeline
  for now.** The detail Sheet shows `CurrentStatus` via `StatusBadge` and every other
  `ShipmentResponse` field, but no event-by-event history. Recorded as a new
  `docs/CLARIFICATIONS.md` entry (§2 below) so it isn't silently dropped.
- **`GET /api/v1/shipments` only supports `page`/`pageSize`** — no status, branch, date-range, or
  AWB/recipient search filter server-side. **Resolved: pagination only for now**, no filter UI.
  Same `CLARIFICATIONS.md` entry.

## 2. `CLARIFICATIONS.md` entry to add

A new numbered entry (next number after the existing highest, §13, is §14) documenting both gaps
above, their resolution, and what unblocks each (a `GET /api/v1/shipments/{awb}/events` endpoint
projecting `ShipmentEvent` for staff view; query params on the list endpoint) — added as part of
Task 1 of the implementation plan, not deferred to the end.

## 3. Routing and panel state

Per `docs/ARCHITECTURE.md` §9a, there is exactly one new route:

```text
/shipments           list — DataTable of accessible shipments, permission shipment.read
```

Create and detail are **Sheets**, not routes — a new `features/shipments/store/
shipmentPanelStore.ts` mirrors `branchPanelStore.ts` exactly:

```typescript
type ShipmentPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; awb: string };  // keyed by AWB, not a Guid id — GET /{awb} is how the
                                       // backend reads a shipment, and it's the natural
                                       // human-facing identifier for this domain (CLAUDE.md §9:
                                       // "AWB is a business identifier")
```

`ShipmentCreateSheet` and `ShipmentDetailSheet` mount once, globally, in `app/(staff)/layout.tsx`
alongside the existing branch sheets — any component in the staff app (the list page, a future
dashboard "recent shipments" quick-link) can open them via the store.

## 4. Shipment list

A `DataTable` fed by a new BFF route `GET /api/shipments` (thin proxy, `page`/`pageSize` query
params only — matching the real backend contract) and a `useShipments(page, pageSize)` hook.

Columns:

```text
AWB          — plain text, monospace-friendly; clicking it (or the row) calls openDetail(awb)
Recipient    — RecipientName
Status       — StatusBadge, resolves via the existing statusConfig.ts (already covers all 16
               ShipmentStatus values — Created through RtoDelivered — no changes needed there)
Service Type — resolved client-side against the service-types reference list (§5) by
               ServiceTypeId -> Name; ServiceTypeResponse.Code is an unconverted enum (serializes
               as a raw integer, same latent bug class as BranchType before its fix — see
               ARCHITECTURE.md §6) so the list/form only ever consume ServiceTypeResponse.Name/Id,
               never .Code, sidestepping it entirely rather than needing another narrow backend fix
COD Amount   — currency-formatted (Intl.NumberFormat, matching FinancialSummary's convention)
Created At   — date-formatted
```

A "New Shipment" button (permission `shipment.create`, hidden otherwise via `usePermission`) calls
`openCreate()`.

## 5. Reference data hooks

Three new thin BFF-proxied hooks, all read-only, all fetched once and cached by TanStack Query
(they change rarely):

```text
useServiceTypes()  -> GET /api/pricing/service-types   -> ServiceTypeResponse[] ({id, code, name})
useMerchants()     -> GET /api/merchants                -> MerchantResponse[] ({id, displayName, ...})
```

(`useBranches()` already exists from Phase F2 and is reused directly for Origin/Destination
pickers.)

## 6. Create Sheet

`ShipmentCreateSheet.tsx` — one scrollable, sectioned form (not a multi-step wizard, matching
Branches' precedent — CLAUDE.md §19/ARCHITECTURE.md §19 both establish "sections, not a wizard, no
carve-out for complex forms getting a full page").

**Fields, exactly matching `CreateShipmentRequest`:**

```text
Route
  Merchant          — Select, useMerchants()
  Origin Branch     — Select, useBranches()
  Destination Branch — Select, useBranches()
  Service Type      — Select, useServiceTypes() (label = Name, value = Id)

Package
  Weight (kg)       — number input
  Length / Width / Height (cm) — optional number inputs
  Items             — repeatable rows (Description, Quantity, Unit Value), add/remove; at least
                      one row required (schema-enforced) since DeclaredValue is computed
                      server-side from these — an empty Items array would price/declare a
                      shipment as worthless

Money
  COD Amount        — number input
  Wants Insurance   — checkbox

Recipient           — GeographyPicker (reused verbatim from Branches) + Name, Phone, Address
Sender               — a second, independent GeographyPicker instance + Name, Phone, Address
```

**Live quote preview:** a read-only price breakdown (base rate, applied charges, subtotal, total —
directly from `PriceQuoteResponse`) that recalculates via `POST /api/pricing/quote` whenever
Origin/Destination/ServiceType/Weight/CodAmount/WantsInsurance change and are all present,
debounced 500ms to avoid a request per keystroke. `PriceQuoteRequest.DeclaredValue` — a field
`CreateShipmentRequest` itself does *not* take — is computed client-side as
`sum(items.quantity * items.unitValue)` purely for this preview call; it is never sent to the
actual `POST /api/v1/shipments` create call, since the backend derives declared value from `Items`
at creation time itself. The preview is clearly labeled as an estimate that recalculates, per
CLAUDE.md §33 — the frontend never treats this number as authoritative or lets the user edit it;
only the backend's response to the actual create call is the real, charged price.

**Submit:** `useCreateShipment()` posts to `POST /api/shipments`; on success, `reset()` the form
and `openDetail(created.awb)` — switching panels in place, matching Branches' create-then-view
flow exactly.

## 7. Detail Sheet

`ShipmentDetailSheet.tsx` — reads `GET /api/shipments/{awb}`, gated by `enabled: awb !== ""` (same
pattern as `useBranch`/`useBranchDashboard`, since this Sheet is globally mounted).

```text
Header    — AWB, StatusBadge(CurrentStatus), Service Type (resolved via useServiceTypes()),
            MerchantId (resolved via useMerchants() — display DisplayName, not the raw Guid)
Summary   — Origin/Destination Branch (resolved via useBranches()), Recipient (name/phone/
            address), Sender (name/phone/address), Package (weight, dimensions if present)
Financial — Declared Value, COD Amount, Delivery/Insurance/Other Charges, Total Charge —
            currency-formatted, matching FinancialSummary's convention
Items     — a simple read-only list (description, quantity, unit value)
```

No tracking timeline (§1). No edit actions this phase — `ShipmentsEndpoints.cs` exposes no generic
update, only `cancel` and the internal `receive-origin` scan transition; a Cancel action is
explicitly **out of scope for F3** (it's a destructive, confirmation-gated action per
ARCHITECTURE.md §22, and belongs with the rest of the lifecycle-transition UI in a later phase
that also handles scanning/manifests — bolting one transition action on in isolation here would be
inconsistent with how every other transition is presented).

## 8. BFF routes

All thin proxies, matching every prior phase's established pattern exactly (`apiFetch` server-side,
`NextResponse.json(body, { status })`):

```text
app/api/shipments/route.ts              GET (list, page/pageSize), POST (create)
app/api/shipments/[awb]/route.ts        GET (detail)
app/api/pricing/quote/route.ts          POST
app/api/pricing/service-types/route.ts  GET
app/api/merchants/route.ts              GET
```

## 9. Permissions

```text
shipment.read    — list page, detail sheet
shipment.create  — "New Shipment" button, create sheet
```

No `shipment.cancel` usage this phase (§7 — cancel deferred).

## 10. Testing approach

Matches every prior phase: BFF route tests (mocked `apiFetch`), hook tests where there's real
logic beyond a bare `useQuery` wrapper (the quote-debounce hook, the declared-value computation),
component tests for the create Sheet's validation and the detail Sheet's field rendering — not
exhaustive snapshot coverage of every Select, matching this project's established test-writing
density.
