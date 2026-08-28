# API Pagination (High-Volume Lists) + CSV Export Streaming — Design Spec

**Scope note:** this is a performance change, not a new phase. It touches four
existing endpoints across three modules (Shipments, Cod, Ndr) plus the two
CSV export endpoints in Reports (Phase 13). No new module, no ROADMAP.md entry.
`src/web` is untouched — no frontend consumers of these endpoints exist yet
(verified: `src/web/src` has only `app/` scaffolding, no `features/` folder).

## 1. Context

Verified against current code: **no list endpoint in this codebase paginates,
and none of the in-scope queries have an `OrderBy` at all.**

| Endpoint | Service method | Row growth driver |
|---|---|---|
| `GET /api/v1/shipments` | `ShipmentService.ListAsync` (`ShipmentService.Reads.cs:25`) | every shipment ever created, org- or merchant-wide |
| `GET /api/v1/cod/collections` | `CodService.ListCollectionsAsync` (`CodService.Reads.cs:47`) | every COD collection ever recorded |
| `GET /api/v1/ndr` | `NdrService.ListAsync` (`NdrService.Reads.cs:22`) | every NDR case ever opened |
| `GET /api/v1/reports/export/shipments` | `ReportExportService.GetShipmentsCsvAsync` | date-range-filtered but still returns `Task<string>` — whole CSV built in memory |
| `GET /api/v1/reports/export/cod-summary` | `ReportExportService.GetCodSummaryCsvAsync` | same in-memory-string pattern |

Explicitly **out of scope**: `GET /api/v1/cod/reconciliation`
(`CodService.GetReconciliationSummaryAsync`) returns a single aggregate
`CodReconciliationSummaryResponse`, not a row list — pagination doesn't apply.
Every other list-shaped endpoint in the repo (Manifests, Merchants, Riders,
Webhooks, Branches, Users, Pricing reference data) is left unpaginated for now;
they are lower current row-count risk and can follow the same pattern later
if/when needed.

Fixing N+1 queries (e.g. `ShipmentService.ListAsync`'s per-shipment
`ShipmentItem` lookup loop), missing indexes, and `AsNoTracking()` gaps on
these same services is explicitly **deferred to a follow-up perf-audit pass**,
matching the precedent already set in the Reports module (commit `49a7b22`).
This change is pagination + export streaming only.

## 2. Shared building block

New types in `src/SharedKernel`:

```csharp
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PaginationDefaults
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
```

One shared envelope, used by every paginated endpoint in this change:

```json
{
  "items": [ ... ],
  "page": 1,
  "pageSize": 25,
  "totalCount": 4213,
  "totalPages": 169
}
```

`page`/`pageSize` are bound as nullable query params on each `MapGet` lambda
(matching existing convention — e.g. `status`/`branchId` on
`/cod/collections` today) and normalized via `PaginationDefaults.Normalize`
inside the service method, not the endpoint — keeps the clamping rule in one
place and unit-testable without an `HttpContext`.

## 3. Per-endpoint changes

For each of `ShipmentService.ListAsync`, `CodService.ListCollectionsAsync`,
`NdrService.ListAsync`:

1. Add `int? page, int? pageSize` parameters.
2. After all existing tenant/branch/merchant/filter `.Where()` clauses are
   applied (unchanged), add a stable sort:
   `.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)` — `CreatedAt` (or
   the closest existing equivalent column) for recency, `Id` as a tiebreaker
   so rows with identical timestamps still sort deterministically. This is
   mandatory: without a deterministic order, Postgres `OFFSET/LIMIT` across
   two requests can return duplicate or skipped rows as concurrent writes land
   between page fetches.
3. Run `CountAsync()` on the filtered-but-unpaged query for `TotalCount`,
   then `.Skip((page - 1) * pageSize).Take(pageSize)` for the page itself —
   two queries against the same filtered `IQueryable`, not one over-fetch.
4. Wrap the result in `PagedResult<T>` and return that from the service
   method (return type changes from `Task<List<T>>` to
   `Task<PagedResult<T>>`); the endpoint's `Results.Ok(...)` wrapping is
   unaffected.

`NdrService.ListAsync` needs a `CreatedAt`-equivalent column check first — if
NDR only carries an `OpenedAt`/similar field under a different name, sort on
that instead; confirm against `Ndr.Domain` during implementation rather than
assuming the name here.

## 4. CSV export streaming

`ReportExportService.GetShipmentsCsvAsync` / `GetCodSummaryCsvAsync` currently
return `Task<string>`, and `ReportsEndpoints.HandleCsvAsync` writes that whole
string via `Results.Text`. Change:

- Service methods change from `Task<string>` to `IAsyncEnumerable<string>`
  (yielding one CSV line at a time: header row first, then one row per
  shipment/COD record from the existing EF Core query via `AsAsyncEnumerable()`
  instead of `ToListAsync()`).
- `ReportsEndpoints.HandleCsvAsync` changes from building/returning a single
  `Results.Text(csv, "text/csv")` to writing directly to
  `HttpContext.Response` (`Content-Type: text/csv`, then
  `await foreach` writing each line to `Response.Body`), so the process never
  holds more than one row's worth of CSV text in memory regardless of how
  many shipments/COD records match the date range.
- No `page`/`pageSize` params on these two endpoints — a CSV export is
  inherently a full-range dump; the fix here is bounded memory, not bounded
  row count. Existing `from`/`to`/`branchId` filters are unchanged.
- The CSV-injection guard added in commit `49a7b22` must carry over unchanged
  to each yielded line.

## 5. Testing

Integration tests per JSON list endpoint (Shipments, Cod collections, Ndr),
extending each module's existing test fixtures:

- Page 1 and page 2 (with `pageSize` small enough to force ≥2 pages in the
  seeded dataset) return disjoint item sets that together cover all matching
  rows exactly once.
- `totalCount`/`totalPages` match the actual filtered row count.
- Requesting `pageSize` above `MaxPageSize` clamps rather than erroring or
  returning unbounded rows.
- Existing filters (`status`, `branchId`, `merchantId`) and existing
  tenant/branch/merchant authorization scoping (already covered by current
  tests) still hold under pagination — i.e. `TotalCount` reflects the scoped
  set, not the whole org.

For the two export endpoints: a test asserting the response is well-formed
CSV covering all seeded rows (streaming must not truncate or reorder output
relative to today's behavior), plus one asserting memory-shape isn't the
concern of an API test — covered instead by confirming the method signature
is `IAsyncEnumerable<string>` and no intermediate `List<>`/`StringBuilder`
of full-dataset size exists in the diff.

## 6. Out of scope (explicitly deferred)

- N+1 fixes, missing indexes, `AsNoTracking()` audit on the touched services.
- Pagination for Manifests, Merchants, Riders, Webhooks, Branches, Users,
  Pricing reference-data list endpoints.
- Cursor-based pagination (offset-based chosen — data scale doesn't yet
  justify cursor complexity; can revisit per-endpoint later if an org's
  dataset grows large enough that `OFFSET` cost becomes visible).
- Frontend list-page UI (no such UI exists yet in `src/web`).
