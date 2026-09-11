# Design: Audit Log Viewer (Tenant + Platform)

**Date:** 2026-09-11
**Status:** Approved for planning
**Track:** Audit module (closes the read-side gap in CLAUDE.md §28)

## 1. Problem

`AuditLog` (`src/Modules/Audit/Domain/AuditLog.cs`) and its write path are fully built —
`IAuditLogger.LogAsync` is called from ~57 sites across every module (shipments, COD, NDR,
manifests, branches, merchants, riders, users/roles, Platform lifecycle actions), and the
table already carries every field CLAUDE.md §28 asks for (`User`, `OrganizationId`, `Action`,
`EntityType`/`EntityId`, `Before`/`After`, `IpAddress`/`UserAgent`, `Timestamp`,
`IsCrossTenantAction`, `ImpersonatedByUserId`). The `audit.read` permission is already seeded
in `PermissionCatalog.cs:60` — but nothing checks it anywhere.

The read side does not exist as a feature. `IAuditLogReader` has exactly one method,
`ListForOrganizationAsync(organizationId, limit)` — unpaginated, unfiltered, and its
`AuditLogRecord` projection omits `Before`/`After`/`IpAddress`/`UserAgent`. Its only caller is
`PlatformDashboardService.GetOrganizationDetailAsync`, which renders the last 50 rows as a
plain `<ul>` inside the org detail Sheet (`OrganizationDetailSheet.tsx:220-230`) — no table, no
filters, no sort, no drill-down. There is no `Presentation` folder in the Audit module at all,
no tenant-facing endpoint, no Platform cross-tenant list endpoint, and no nav entry anywhere.

## 2. Goals

* A tenant user whose role has `audit.read` gets an "Audit Logs" nav item and a page listing
  their organization's audit trail — searchable, filterable, sortable by date, paginated.
* A `SuperAdmin` gets an equivalent Platform page listing audit events across **all**
  organizations, unrestricted, with an added organization filter.
* Both views expose the full record on drill-down (`Before`/`After`, actor, IP, user agent),
  not just the action/timestamp the current `<ul>` shows.

## 3. Non-Goals

* Branch-scoped visibility. `AuditLog` has no `BranchId` column, and retrofitting one onto all
  ~57 existing `LogAsync` call sites is a separate, much larger effort. v1 visibility is
  permission + organization only — a known limitation, not an oversight.
* Per-domain permission mapping (e.g. `shipment.read` sees only shipment events, `cod.read`
  only COD events). Rejected in favor of the single existing `audit.read` permission gating the
  whole feature — matches how every other nav item in `navItems.ts` is gated by one permission
  (or a small explicit OR-set), and avoids inventing a mapping table for domains that have no
  natural permission (e.g. `organization.*`, `webhook.*`).
* Replacing the existing `OrganizationDetailSheet` "Recent Activity" `<ul>` — left as-is; it's a
  capped preview, not the feature this spec builds. Could later link to the new Platform audit
  page filtered by org, but that's a follow-up, not required here.
* Any change to the write side (`IAuditLogger`, the ~57 call sites, `AuditEntry`) — this spec is
  read-only.
* Retention/archival policy for old audit rows — out of scope.

## 4. Backend — Read Model

`src/Modules/Audit/Application/IAuditLogReader.cs` gains a second method (the existing
`ListForOrganizationAsync` stays, unchanged — `PlatformDashboardService`'s org-detail preview
keeps using it as-is):

```csharp
public record AuditLogRecord(
    Guid Id, string Action, string EntityType, string EntityId,
    Guid? UserId, Guid? ImpersonatedByUserId, bool IsCrossTenantAction,
    DateTimeOffset Timestamp, string? Before, string? After, string? IpAddress, string? UserAgent);

public record AuditLogSearchFilter(
    string? Search, string? Action, string? EntityType, string? EntityId,
    Guid? UserId, DateTimeOffset? DateFrom, DateTimeOffset? DateTo);

Task<PagedResult<AuditLogRecord>> SearchAsync(
    Guid? organizationId, AuditLogSearchFilter filter, int? page, int? pageSize,
    CancellationToken cancellationToken = default);
```

`organizationId` is nullable so one method serves both callers:

* Tenant caller passes the ambient tenant's id explicitly (belt-and-suspenders alongside the
  existing `HasQueryFilter` — mirrors how `ListForOrganizationAsync` already does both).
* Platform caller passes `null` when no org filter is chosen (`IgnoreQueryFilters()`, sees every
  org) or a specific `organizationId` to narrow to one org — same method, no duplication.

Implementation in `AuditLogger.cs`: `IgnoreQueryFilters()` always (tenant scoping is enforced by
the explicit `organizationId` parameter, not the ambient filter — same reasoning already
documented on `ListForOrganizationAsync`), then apply each non-null filter field, `Search`
matching `Action`, `EntityType`, or `EntityId` via `Contains` (case-insensitive collation, same
as `OrganizationLookupService`'s existing search), `OrderByDescending(a => a.Timestamp)`
`.ThenByDescending(a => a.Id)` for a stable sort, then `PaginationDefaults.Normalize` +
`CountAsync` + `Skip`/`Take` — the exact shape `ShipmentService.Reads.ListAsync` and
`PlatformDashboardService.ListAllInvoicesAsync` already use.

## 5. Backend — Endpoints

**Tenant** — new `src/Modules/Audit/Presentation/AuditEndpoints.cs`, registered in `Program.cs`
alongside the other `Map*Endpoints` calls:

```csharp
app.MapGroup("/api/v1/audit-logs").WithTags("Audit").RequireAuthorization()
   .MapGet("/", async (string? search, string? action, string? entityType, string? entityId,
                        Guid? userId, DateTimeOffset? dateFrom, DateTimeOffset? dateTo,
                        int? page, int? pageSize,
                        IAuditLogReader reader, ICurrentTenantProvider tenant, CancellationToken ct) =>
       Results.Ok(await reader.SearchAsync(tenant.OrganizationId, new(search, action, entityType, entityId, userId, dateFrom, dateTo), page, pageSize, ct)))
   .RequirePermission("audit.read");
```

**Platform** — new `src/Modules/Platform/Presentation/PlatformAuditEndpoints.cs` (kept
separate from `PlatformDashboardEndpoints.cs` rather than added to it — that file's existing
surface is org lifecycle + metrics, not a list endpoint; matches how
`PlatformOrganizationsEndpoints`/`PlatformInvoicesEndpoints`-style list endpoints already get
their own file):

```csharp
app.MapGroup("/api/v1/platform/audit-logs").WithTags("Platform Audit")
   .MapGet("/", async (Guid? organizationId, string? organizationSearch, string? search, string? action,
                        string? entityType, string? entityId, Guid? userId,
                        DateTimeOffset? dateFrom, DateTimeOffset? dateTo, int? page, int? pageSize,
                        PlatformAuditService service, CancellationToken ct) =>
       Results.Ok(await service.SearchAsync(organizationId, organizationSearch, search, action, entityType, entityId, userId, dateFrom, dateTo, page, pageSize, ct)))
   .RequireSuperAdmin();
```

`organizationId` (exact match, for a future direct link from `OrganizationDetailSheet`) and
`organizationSearch` (org-name substring) are both optional and independent — `organizationId`
takes precedence when both are given. A new thin `Platform.Application.PlatformAuditService`
resolves `organizationSearch` to matching org IDs first (same `IOrganizationLookupService.ListAsync`
+ `HashSet` narrowing `PlatformDashboardService.ListAllInvoicesAsync` already does for its own
`search` param), calls `IAuditLogReader.SearchAsync`, then attaches `OrganizationName` per row via
the same bounded, per-page `GetAsync` loop `ListAllInvoicesAsync` uses — returns
`PagedResult<AuditLogRecordWithOrgName>` (`AuditLogRecord` + `OrganizationName`).

## 6. Frontend — Tenant

* `navItems.ts`: one new entry, `{ href: "/audit-logs", label: "Audit Logs", permission:
  "audit.read", icon: History }` — `nav-main.tsx`'s existing `usePermission` check hides it
  automatically for anyone without the permission; no new gating code.
* New route `src/web/src/app/(staff)/audit-logs/page.tsx` — same route group as the existing
  `/ndrs`, `/cod`, `/branches` pages.
* New `src/web/src/features/audit-logs/`: `hooks/useAuditLogs.ts` (TanStack Query,
  `URLSearchParams` + `clientFetch<PagedResult<AuditLogRecord>>`, same shape as
  `usePlatformInvoices`), a `DataTable` column set (Timestamp, Action, EntityType/Id, Actor),
  debounced search input (`useDebouncedValue`, 300ms), an entity-type `Select` filter, a date
  range filter (shadcn `DatePicker`, per this project's component convention), and default sort
  `Timestamp desc` (matches "sort by date" requirement — no separate sort-direction UI needed
  for v1 since desc-by-default is the only ordering asked for; flag at plan time if an
  asc/desc toggle is wanted).
* Row click opens a right-side `Sheet` (this project's detail convention, never a routed page)
  showing the full record: `Before`/`After` (rendered as a diff or two JSON blocks), actor,
  `IpAddress`, `UserAgent`.

## 7. Frontend — Platform

* `PlatformNav.tsx`: add "Audit Logs" alongside Dashboard/Organizations/Plans/Invoices.
* New `src/web/src/app/platform/audit-logs/page.tsx` + `src/web/src/features/platform-audit-logs/`
  mirroring `platform-invoices` exactly: same filters as tenant, plus a debounced organization-name
  search input (bound to the `organizationSearch` query param — no separate org picker component
  needed, consistent with how `platform-invoices` already resolves its own `search` term to
  matching orgs server-side) and an `OrganizationName` column.
* Same row-click-to-Sheet detail pattern as tenant.

## 8. Testing

* **Unit** (`Audit.Tests`): `SearchAsync` filter combinations (search, action, entityType,
  entityId, userId, date range), sort stability, pagination math — extends the existing
  `AuditLoggerTests.cs`.
* **Integration**: tenant isolation (org A's `SearchAsync(orgA.Id, ...)` never returns org B's
  rows even when both exist in the same in-memory/test DB); `audit.read`-gated endpoint returns
  403 without the permission and 200 with it.
* **API**: `AuditEndpoints` — auth required, permission required, pagination/sort/filter params
  round-trip correctly; `PlatformAuditEndpoints` — `RequireSuperAdmin` enforced, cross-org rows
  visible, `organizationId` filter narrows correctly.
* **Frontend**: nav item visibility test mirroring `nav-main.test.tsx` (hidden without
  `audit.read`, shown with it); page-level test for the table rendering a page of results and
  advancing pagination (mirroring the existing `platform-invoices`/`platform-organizations`
  page tests).
