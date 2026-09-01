# Frontend Phase F8 — Reports & Analytics — Design

**Date:** 2026-09-01
**Status:** Approved, ready for implementation planning.
**Roadmap entry:** `docs/FRONTEND-ROADMAP.md` Phase F8.

## Context

F0–F7 are complete. Phase 13 (backend) shipped `Courier.Reports`: `GET
/api/v1/reports/dashboard/organization` (accepts optional `from`/`to`, defaults
to the last 30 days), `GET /api/v1/reports/dashboard/branch/{id}`, and two
streaming-CSV export routes, `GET /api/v1/reports/export/shipments` and `GET
/api/v1/reports/export/cod-summary` (both accept optional `branchId`/`from`/`to`).
Frontend Phase F1 already built the `/dashboard` page consuming the
organization-dashboard endpoint (`ShipmentFunnel`, `FinancialSummary`,
`RateTiles`, `DeliveryRateHero`, `BranchPerformanceChart`), and Frontend Phase F2
already wired the branch-dashboard endpoint into the branch detail Sheet
(`BranchShipments`/`BranchExceptions`). F1's own notes explicitly deferred rider
performance and export UI to this phase.

Investigating `src/Modules/Reports/` and `src/web/src/features/reports/` before
design turned up the real, concrete gaps this phase closes — not a guess at what
might be missing:

1. **`OrgDashboardResponse.RiderPerformance` is already returned by the backend
   but silently dropped by the frontend.** `ReportsDtos.cs`'s
   `OrgDashboardResponse` has carried an `IReadOnlyList<RiderPerformance>`
   (`RiderId`, `Attempts`, `Successes`, `SuccessRate`) since Phase 13 shipped;
   `src/web/src/features/reports/types.ts`'s `OrgDashboardSummary` has no
   `riderPerformance` field at all, and `DashboardPage` never renders it.
2. **The organization dashboard has no date-range control.** The backend
   endpoint already accepts optional `from`/`to` (`ReportsDateRange.Resolve`,
   defaulting to the trailing 30 days, capped at 366 days), but
   `useOrgDashboard()` takes no parameters and the BFF route
   (`app/api/reports/dashboard/organization/route.ts`) doesn't forward a query
   string at all. F1's own polish pass added a "Last 30 days" caption to
   `RateTiles` precisely because this was hardcoded with no way to change it.
3. **Zero frontend UI exists for either CSV export endpoint.** `report.export`
   is already a real permission with real backend routes; nothing in `src/web`
   calls them.
4. **Neither CSV export endpoint sets `Content-Disposition`.**
   `ReportsEndpoints.HandleCsvAsync` sets `Response.ContentType = "text/csv"`
   only. Without `Content-Disposition: attachment`, most browsers will try to
   render the CSV as a page (or hand it to whatever is registered for
   `text/csv`) instead of downloading it — a real bug that would make this
   phase's own export UI feel broken the moment someone clicks it, so it's
   fixed as part of this phase rather than deferred.

No other backend changes are needed — `BranchPerformance`'s dictionary-of-
`ShipmentStatus`-things pattern already works correctly end-to-end (`ShipmentStatus`
got its `JsonStringEnumConverter` fix back in Frontend Phase F3), and the branch
dashboard endpoint is already fully consumed by Phase F2's branch detail Sheet —
this phase does not touch it again.

## Backend change

### `Content-Disposition` on both CSV export routes

In `src/Modules/Reports/Presentation/ReportsEndpoints.cs`, `HandleCsvAsync` gains
a `fileNamePrefix` parameter so each call site supplies its own name:

```csharp
private static async Task<IResult> HandleCsvAsync(HttpContext httpContext, string fileNamePrefix, Func<Task<IEnumerable<string>>> operation)
{
    IEnumerable<string> lines;
    try
    {
        lines = await operation();
    }
    catch (ReportsValidationException ex)
    {
        return Results.BadRequest(new { error = new { code = "REPORTS_VALIDATION_FAILED", message = ex.Message } });
    }
    catch (InvalidDateRangeException ex)
    {
        return Results.BadRequest(new { error = new { code = "INVALID_DATE_RANGE", message = ex.Message } });
    }

    httpContext.Response.ContentType = "text/csv";
    httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileNamePrefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv\"";
    await using var writer = new StreamWriter(httpContext.Response.Body, leaveOpen: true);
    foreach (var line in lines)
    {
        await writer.WriteAsync(line);
    }
    await writer.FlushAsync();
    return Results.Empty;
}
```

The two call sites pass `"shipments"` and `"cod-summary"` respectively. A
timestamped filename (not a static `"shipments.csv"`) means repeated exports in
the same browser session don't silently collide in the Downloads folder.

## Frontend structure

All additions land in the existing `src/web/src/features/reports/` folder and
the existing `/dashboard` page — no new nav entry, no new route. This matches
`docs/FRONTEND-ROADMAP.md`'s own framing of F8 as filling out the dashboard
Phase F1 started, not a separate surface.

### 1. `riderPerformance` end to end

`src/web/src/features/reports/types.ts` gains:

```typescript
export interface RiderPerformance {
  riderId: string;
  attempts: number;
  successes: number;
  successRate: number | null;
}
```

`OrgDashboardSummary` gains `riderPerformance: RiderPerformance[]`.

### 2. Date range on the organization dashboard

`useOrgDashboard(params?: { from?: string; to?: string })` builds a query
string exactly like every other filtered hook in this codebase
(`useCodCollections`-style `URLSearchParams`), and the BFF route forwards
`request.nextUrl.search` unchanged (matching `app/api/cod/collections/route.ts`'s
established pattern — the current organization-dashboard BFF route is the one
outlier in this codebase that drops query strings, from before any dashboard
filter existed).

`DashboardPage` gets two `<Input type="date">` controls (From/To) above the
existing grid, defaulting to empty (letting the backend's own 30-day default
apply). Submitted values are anchored to explicit UTC before reaching the query
string — `${value}T00:00:00Z` / `${value}T23:59:59Z` — the exact fix Frontend
Phase F7 needed for `MerchantSettlementCreateSheet`, applied proactively here
instead of waiting to hit the same Nepal-host/Npgsql-offset failure again.

### 3. Rider Performance card

New `src/web/src/features/reports/components/RiderPerformanceChart.tsx`,
structurally identical to `BranchPerformanceChart.tsx` (same bar chart + sorted
detail-rows-below pattern, same `EmptyState` empty case) but keyed by rider and
labeled by `licenseNumber` via `useRiders()` (`@/features/riders/hooks/useRiders`)
— riders have no display-name field on `RiderResponse` today, so this matches
every other rider-identifying UI in the app (COD's Rider Settlements tab,
`AssignTaskSheet`). Chart bars plot `attempts`; the detail rows below show
`successes`/`attempts` and the formatted `successRate`, sorted by `attempts`
descending (busiest riders first, mirroring `BranchPerformanceChart`'s
volume-descending sort).

Added to `DashboardPage`'s existing first grid row, alongside `ShipmentFunnel`
and `BranchPerformanceChart` (that row becomes 3 columns on `xl:`, still stacking
to 1 column on mobile — no other row's layout changes).

### 4. Export Reports card

New `src/web/src/features/reports/components/ExportReportsCard.tsx`: one `Card`
with a branch picker (`useBranches()`, "All branches" default — independent of
the page-level date range, since exports are their own filtered action, not a
view of the already-loaded dashboard data) and its own From/To date inputs
(same UTC-anchoring as above), plus two buttons, "Export Shipments (CSV)" and
"Export COD Summary (CSV)". Each button is a plain link
(`<a href="/api/reports/export/shipments?...">`, not a `fetch`) pointing at a
new BFF passthrough route — browser navigation to a same-origin URL carries the
existing session cookie automatically, so the Next.js Route Handler's own
`apiFetch` (which reads that session to attach the bearer token server-side)
needs no client-side wiring at all; a `fetch`-and-blob approach would only add
complexity here for no benefit. Gated on `usePermission("report.export")` —
CLAUDE.md §33's "never trust the frontend" principle means this is UI polish
only (the backend's own `RequirePermission("report.export")` is the real gate),
but hiding controls a user can't use is standard practice everywhere else in
this app (e.g. `canCreate`/`canUpdate` checks throughout).

Two new BFF routes, both pure passthroughs preserving the CSV response body and
headers (`Content-Type`, `Content-Disposition`) rather than the JSON-parsing
pattern every other BFF route in this codebase uses, since these must remain
streamable files, not `NextResponse.json(...)`:

`src/web/src/app/api/reports/export/shipments/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/reports/export/shipments${query}`);
    return new NextResponse(response.body, {
      status: response.status,
      headers: {
        "Content-Type": response.headers.get("Content-Type") ?? "text/csv",
        "Content-Disposition": response.headers.get("Content-Disposition") ?? "attachment",
      },
    });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

`src/web/src/app/api/reports/export/cod-summary/route.ts` is identical except
for the upstream path.

Added to `DashboardPage` as its own `Card` at the bottom of the page, after
`FinancialSummary`.

## Testing

- **Backend**: a unit test on `ReportsEndpoints`'s `HandleCsvAsync` behavior
  (via the existing `Api.Tests` integration-test pattern) asserting both export
  routes now return a `Content-Disposition: attachment` header containing
  `.csv`.
- **Frontend**: Vitest tests for `useOrgDashboard`'s query-string construction
  (mirroring `useCodCollections`'s own test-free-but-typed precedent — add a
  test here since this hook is changing shape, unlike a brand-new hook),
  `RiderPerformanceChart`'s empty-state and sorted-rows rendering (mirroring
  `BranchPerformanceChart.test.tsx`), and `ExportReportsCard`'s permission-gated
  rendering and generated `href` query strings (branch id + UTC-anchored dates).
- **Manual E2E** against the real API/Postgres before the phase is called
  done — every prior phase's own pass found real bugs invisible to unit tests
  alone. Confirm: the dashboard renders `riderPerformance` for a rider who has
  completed at least one delivery attempt; changing the date range actually
  changes the numbers shown (bound it tightly enough to exclude/include a known
  attempt); clicking each export button downloads (not opens-inline) a
  correctly-named, non-empty CSV file with the expected header row; export
  respects the branch filter (a branch-scoped export excludes another branch's
  shipments).

## Definition of Done (CLAUDE.md §37, applied to this phase)

Every Phase 13 reporting/export endpoint has a corresponding UI element; no
hardcoded/demo data paths remain anywhere in `src/web/src/features/reports/`;
permission-gated (`report.read` implicitly via existing dashboard access,
`report.export` for the new export controls); loading/error/empty states on
every new element; backend + frontend tests passing; `docs/FRONTEND-ROADMAP.md`
F8 entry updated with completion notes; build green; no regressions in F0–F7.
