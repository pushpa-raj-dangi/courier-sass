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
| `GET /api/v1/ndrs` | `NdrService.ListAsync` (`NdrService.Reads.cs:22`) | every NDR case ever opened |
| `GET /api/v1/reports/export/shipments` | `ReportExportService.GetShipmentsCsvAsync` | date-range-filtered but still returns `Task<string>` — whole CSV built in memory |
| `GET /api/v1/reports/export/cod-summary` | `ReportExportService.GetCodSummaryCsvAsync` | same in-memory-string pattern |

(Corrected from an earlier draft of this spec: the NDR route is `/api/v1/ndrs`, not `/api/v1/ndr`.)

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

**Verified against the actual domain models: none of `Shipment`, `CODCollection`,
or `NDR` have a `CreatedAt` column, and all three use `Guid.NewGuid()` (random,
not time-ordered) for `Id`.** There is nothing to sort by for a deterministic
"newest first" page order. Section 3a covers the schema fix this requires
before pagination can be correct.

For each of `ShipmentService.ListAsync`, `CodService.ListCollectionsAsync`,
`NdrService.ListAsync`:

1. Add `int? page, int? pageSize` parameters.
2. After all existing tenant/branch/merchant/filter `.Where()` clauses are
   applied (unchanged), add a stable sort:
   `.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)` using the new
   `CreatedAt` column from 3a, `Id` as a tiebreaker so rows with identical
   timestamps still sort deterministically. This is mandatory: without a
   deterministic order, Postgres `OFFSET/LIMIT` across two requests can
   return duplicate or skipped rows as concurrent writes land between page
   fetches.
3. Run `CountAsync()` on the filtered-but-unpaged query for `TotalCount`,
   then `.Skip((page - 1) * pageSize).Take(pageSize)` for the page itself —
   two queries against the same filtered `IQueryable`, not one over-fetch.
4. Wrap the result in `PagedResult<T>` and return that from the service
   method (return type changes from `Task<List<T>>` to
   `Task<PagedResult<T>>`); the endpoint's `Results.Ok(...)` wrapping is
   unaffected.

**Exception — `NdrService.ListAsync` under branch-restricted callers:** `NDR`
has no branch column of its own (documented in `NdrConfiguration.cs`); a
branch-restricted caller's scoping requires a per-row lookup of each NDR's
shipment via `IShipmentTransitionService.GetScanInfoAsync` (already how
today's code works), which cannot be pushed into the SQL query without giving
the Ndr module EF-model access to Shipment — a module-boundary line this
codebase has already deliberately drawn (Phase 9). So:
- **`AllBranches`-scope callers** (`GetAccessibleBranchIdsAsync` returns
  `null`, e.g. `OrganizationOwner`): true SQL-level `Skip/Take`, same as the
  other two endpoints.
- **Branch-restricted callers**: fetch all status-filtered NDRs (as today),
  apply the per-row branch check in memory, then paginate the in-memory
  filtered list. This path does not get the "flat memory regardless of row
  count" property the other endpoints get — it's bounded by an org's total
  NDR count under the given status filter, not by page size. Closing this
  gap properly (e.g. giving `NDR` its own `BranchId` snapshot column, the way
  `CODCollection` already has one) is out of scope for this change; noted in
  §6.

### 3a. Schema change: add `CreatedAt` to `Shipment`, `CODCollection`, `NDR`

One EF Core migration adds a `DateTimeOffset CreatedAt` column (`defaultValueSql:
"now()"` so existing rows backfill instead of erroring on a `NOT NULL` add) to
all three tables. Going forward, each entity's creation path sets it
explicitly:

- `Shipment` — `ShipmentService.cs:145`, inside the AWB-retry loop's object
  initializer (alongside `Id = Guid.NewGuid()`).
- `CODCollection` — `CodService.Collections.cs:15`
  (`CodService.CreateExpectedAsync`), alongside its own `Id = Guid.NewGuid()`.
- `NDR` — `NdrCreationService.cs:34`
  (`NdrCreationService.CreateFromFailedAttemptAsync`), alongside its own
  `Id = Guid.NewGuid()`.

This is a genuine, if small, expansion beyond "no schema changes" — justified
because there is no other reliable creation-order signal on any of the three
entities (`CODCollection.CollectedAt` is nullable and unset until COD is
actually collected; `Id` is random). Adding `CreatedAt` to `Shipment` also
closes a gap CLAUDE.md §9 already calls for and the Phase 13 spec had already
flagged as missing.

## 4. CSV export streaming

**Verified against the actual code, this needs a narrower fix than "stream
straight from EF Core" implies.** `ReportExportService`'s two methods don't
query rows directly — they call `IShipmentReportingLookupService`/
`ICodReportingLookupService` (`GetShipmentExportRowsAsync`/
`GetCodExportRowsAsync`), which already return a fully materialized
`IReadOnlyList<...Row>`, specifically *because* the service then needs to
collect every distinct branch/merchant id across all rows and batch-resolve
their display names in two more calls before it can build any CSV line. That
name-batching pattern is Phase 13's established design (avoids an N+1 of
one name lookup per row) and redesigning it into a true end-to-end DB cursor
is out of scope here. So: the row list stays materialized in memory, same as
today (its size is bounded by the date range filter already in place, and
fixing that further belongs with the deferred N+1/index audit in §6).

What *does* change: today, after the rows are ready, `CsvWriter.Write` builds
the **entire formatted CSV as one `StringBuilder`/`string`** before anything
is returned — for a large date range this string can be substantially bigger
than the row objects themselves (quoting/escaping overhead), and the HTTP
response can't start sending bytes until that whole string exists. Fix:

- `CsvWriter` gets a new `WriteLines(headers, rows) : IEnumerable<string>`
  that yields the header line first, then one CRLF-terminated line per row —
  reusing the same private `EscapeField` (including the CSV-injection guard
  from commit `49a7b22`) the existing `Write` method already uses.
  **`Write` itself is left untouched** — it already has 5 passing unit tests
  in `CsvWriterTests.cs`, including the injection-guard cases, and nothing
  requires removing it.
- `ReportExportService.GetShipmentsCsvAsync`/`GetCodSummaryCsvAsync` change
  from `Task<string>` to `Task<IEnumerable<string>>`, calling `WriteLines`
  instead of `Write` as their last line. Everything before that (row fetch,
  name batch-resolution, branch-scope check) is unchanged.
- `ReportsEndpoints.HandleCsvAsync` changes from building/returning a single
  `Results.Text(csv, "text/csv")` to setting `HttpContext.Response.ContentType`
  and writing each line directly to `Response.Body` via a `StreamWriter`, so
  the process never holds the fully-formatted CSV text as one giant string —
  only the already-materialized row objects plus one line at a time of text.
- No `page`/`pageSize` params on these two endpoints — a CSV export is
  inherently a full-range dump; existing `from`/`to`/`branchId` filters are
  unchanged.

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

For the two export endpoints: unit tests on `CsvWriter.WriteLines` (new,
alongside the existing `Write` tests) proving it yields the same header +
row content `Write` would have returned, including the CSV-injection-guard
case. The existing `ReportsEndpointsTests.cs` export tests (content-type,
header-row prefix, cross-tenant `branchId` rejection) need no changes and
must still pass — they assert on the HTTP response body, which is identical
whether that body was assembled as one string or streamed line-by-line.

## 6. Out of scope (explicitly deferred)

- N+1 fixes, missing indexes, `AsNoTracking()` audit on the touched services.
- Pagination for Manifests, Merchants, Riders, Webhooks, Branches, Users,
  Pricing reference-data list endpoints.
- Cursor-based pagination (offset-based chosen — data scale doesn't yet
  justify cursor complexity; can revisit per-endpoint later if an org's
  dataset grows large enough that `OFFSET` cost becomes visible).
- Frontend list-page UI (no such UI exists yet in `src/web`).
- Giving `NDR` its own `BranchId` column so branch-restricted callers get
  true SQL-level pagination on `GET /api/v1/ndrs` (§3's noted exception) —
  today's in-memory-filter behavior continues, just with pagination applied
  after it instead of not at all.
- Full end-to-end DB-cursor streaming for the two CSV export endpoints — §4's
  fix bounds the formatted-text memory, not the already-materialized row-list
  memory, which would require redesigning `ReportExportService`'s
  name-batching pattern.
- True historical accuracy of `CreatedAt` for rows that existed before this
  migration runs — they backfill to the migration's apply time (`now()`),
  not their real creation time. Deterministic pagination and "recent items
  first" ordering both still hold; only "what did this shipment's exact
  booking timestamp used to be" is unrecoverable for pre-existing rows, and
  nothing before this change exposed or relied on that value anyway.
