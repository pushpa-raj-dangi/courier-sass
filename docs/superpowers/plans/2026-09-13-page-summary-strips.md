# Page Summary Strips Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a row of branch-scoped status-breakdown stat tiles above each of the 7 core staff list pages (Shipments, Branches, Riders, Manifests, NDRs, Return Shipments, COD).

**Architecture:** One shared frontend tile component (`SummaryStatsBar`) reused by 7 new hooks, each backed by a new lightweight `GET .../summary` endpoint added to that module's own Presentation file (never through the Reports module), gated by that module's own existing read permission. Every new backend method reuses each module's already-injected `IBranchAuthorizationChecker`/`DbContext` — no new constructor dependencies anywhere, so no existing test breaks.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core, xUnit (backend); Next.js App Router, TanStack Query, Vitest + Testing Library (frontend); shadcn/ui (`Card`, `Skeleton`).

**Spec:** `docs/superpowers/specs/2026-09-13-page-summary-strips-design.md`

## Global Constraints

* Every new backend endpoint requires that page's own existing read permission (`shipment.read`, `branch.read`, `rider.read`, `manifest.read`, `ndr.read`, `rto.read`, `cod.read`) — never `report.read`.
* Branch scoping always uses `IBranchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, permission, ct)` — `null` means AllBranches (no filter), a non-null set restricts. Never invent a different convention per task; each task below states the exact field(s) to filter on, verified against that module's existing list-scoping code.
* Summary counts are branch-scoped totals only — no date range, no reaction to table filters/search/pagination.
* Every frontend hook calls a Next.js BFF proxy route (`src/web/src/app/api/<x>/summary/route.ts`), never the .NET API directly — matches every existing hook in this codebase.
* Tile colors reuse the existing `--status-*` Tailwind color tokens (`bg-status-created/15 text-status-created`, etc. — all of `status-created`, `status-in-transit`, `status-out-for-delivery`, `status-delivered`, `status-ndr`, `status-rto`, `status-sealed`, `status-received`, `status-cancelled` are already registered in `tailwind.config.ts`) wherever a page's counts are literal status buckets; pages with no natural status dimension (Branches, Riders) use neutral (`bg-muted`) or accent (`bg-accent text-primary`) tiles, matching `QuickStatsStrip.tsx`'s existing non-status tiles.
* Each page's existing `if (isLoading) return <Spinner/>` / `if (isError) return <ErrorState/>` early-return pattern must be restructured so the summary strip (with its own independent loading/error state) always renders above the table, never blocked by the table's own load — the table's existing early-return guards move to wrap only the table portion.
* No changes to the Reports module, no changes to any existing constructor, no changes to any existing test file's expectations.

---

## Task 1: Shared `SummaryStatsBar` Component

**Files:**
- Create: `src/web/src/components/summary-stats/SummaryStatsBar.tsx`
- Test: `src/web/src/components/summary-stats/SummaryStatsBar.test.tsx`

**Interfaces:**
- Produces: `StatTile` type and `SummaryStatsBar` component, imported by every page task (2-8) as `import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar"`.

```ts
export interface StatTile {
  label: string;
  value: string;
  icon: LucideIcon;
  badgeClass: string; // e.g. "bg-status-delivered/15 text-status-delivered"
  highlight?: boolean;
}

export function SummaryStatsBar(props: { tiles: StatTile[]; isLoading: boolean; isError: boolean; skeletonCount?: number }): JSX.Element
```

- [ ] **Step 1: Write the failing test**

```tsx
// src/web/src/components/summary-stats/SummaryStatsBar.test.tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { SummaryStatsBar, type StatTile } from "./SummaryStatsBar";

const tiles: StatTile[] = [
  { label: "Created", value: "12", icon: Package, badgeClass: "bg-status-created/15 text-status-created" },
  { label: "Delivered", value: "40", icon: Package, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
];

describe("SummaryStatsBar", () => {
  it("renders a tile per entry with its label and value", () => {
    render(<SummaryStatsBar tiles={tiles} isLoading={false} isError={false} />);
    expect(screen.getByText("Created")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("Delivered")).toBeInTheDocument();
    expect(screen.getByText("40")).toBeInTheDocument();
  });

  it("renders skeleton placeholders while loading instead of tiles", () => {
    render(<SummaryStatsBar tiles={[]} isLoading={true} isError={false} skeletonCount={3} />);
    expect(screen.queryByText("Created")).not.toBeInTheDocument();
    expect(screen.getAllByTestId("summary-stat-skeleton")).toHaveLength(3);
  });

  it("renders nothing but does not throw when errored", () => {
    const { container } = render(<SummaryStatsBar tiles={[]} isLoading={false} isError={true} />);
    expect(container.querySelector('[data-testid="summary-stats-error"]')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `src/web`): `npx vitest run src/components/summary-stats/SummaryStatsBar.test.tsx`
Expected: FAIL — `Cannot find module './SummaryStatsBar'`

- [ ] **Step 3: Write minimal implementation**

```tsx
// src/web/src/components/summary-stats/SummaryStatsBar.tsx
import type { LucideIcon } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

export interface StatTile {
  label: string;
  value: string;
  icon: LucideIcon;
  badgeClass: string;
  highlight?: boolean;
}

export function SummaryStatsBar({
  tiles,
  isLoading,
  isError,
  skeletonCount = 4,
}: {
  tiles: StatTile[];
  isLoading: boolean;
  isError: boolean;
  skeletonCount?: number;
}) {
  if (isError) {
    return (
      <p data-testid="summary-stats-error" className="text-sm text-muted-foreground">
        Summary unavailable right now.
      </p>
    );
  }

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {Array.from({ length: skeletonCount }).map((_, i) => (
          <Skeleton key={i} data-testid="summary-stat-skeleton" className="h-[68px] w-full rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
      {tiles.map((tile) => {
        const Icon = tile.icon;
        return (
          <Card key={tile.label}>
            <CardContent className="flex items-center gap-3 py-4">
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${tile.badgeClass}`}>
                <Icon className="h-5 w-5" strokeWidth={2} />
              </span>
              <div className="flex flex-col gap-0.5 overflow-hidden">
                <span className="truncate text-xs text-muted-foreground">{tile.label}</span>
                <span
                  className={`truncate font-sans text-lg font-semibold sm:text-xl ${
                    tile.highlight ? "text-primary" : "text-foreground"
                  }`}
                >
                  {tile.value}
                </span>
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/components/summary-stats/SummaryStatsBar.test.tsx`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/web/src/components/summary-stats
git commit -m "feat(web): add shared SummaryStatsBar component"
```

---

## Task 2: Shipments Summary

**Files:**
- Modify: `src/Modules/Shipments/Application/IShipmentReportingLookupService.cs`
- Modify: `src/Modules/Shipments/Application/ShipmentReportingLookupService.cs`
- Modify: `src/Modules/Shipments/Presentation/ShipmentsEndpoints.cs`
- Test: `tests/Shipments.Tests/ShipmentReportingLookupServiceTests.cs` (append)
- Test: `tests/Api.Tests/ShipmentsEndpointsTests.cs` (append)
- Create: `src/web/src/app/api/shipments/summary/route.ts`
- Test: `src/web/src/app/api/shipments/summary/route.test.ts`
- Create: `src/web/src/features/shipments/hooks/useShipmentsSummary.ts`
- Test: `src/web/src/features/shipments/hooks/useShipmentsSummary.test.tsx`
- Modify: `src/web/src/app/(staff)/shipments/page.tsx`

**Interfaces:**
- Consumes: `SummaryStatsBar`/`StatTile` from Task 1.
- Produces: `GET /api/v1/shipments/summary` → `IReadOnlyDictionary<ShipmentStatus, int>` (JSON object keyed by PascalCase status name, e.g. `{"Created": 3, "InTransit": 5}` — missing keys mean zero, per `GetCohortStatusCountsAsync`'s existing dictionary-building convention). `useShipmentsSummary()` hook returning that same shape client-side.

### Backend

- [ ] **Step 1: Write the failing unit test**

Append to `tests/Shipments.Tests/ShipmentReportingLookupServiceTests.cs` (inside the existing `ShipmentReportingLookupServiceTests` class, using its existing `FixedTenantProvider`/`SeedOrgAsync`/`BuildShipment` helpers already in that file):

```csharp
    [Fact]
    public async Task GetCurrentStatusCountsAsync_GroupsByCurrentStatus_NoBranchFilter()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Delivered));
        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.Created));
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCurrentStatusCountsAsync(null);

        Assert.Equal(2, result[ShipmentStatus.Delivered]);
        Assert.Equal(1, result[ShipmentStatus.Created]);
    }

    [Fact]
    public async Task GetCurrentStatusCountsAsync_BranchRestricted_IncludesShipmentWithNullCurrentBranchIdViaOriginOrDestination()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        // OutForDelivery shipment has CurrentBranchId == null (with a rider) -- must still count
        // toward its origin branch's restricted total via the OriginBranchId arm of the filter,
        // not be silently dropped the way a bare CurrentBranchId-only filter would drop it.
        var outForDelivery = BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.OutForDelivery, currentBranchId: null);
        dbContext.Add(outForDelivery);
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCurrentStatusCountsAsync(new HashSet<Guid> { originBranchId });

        Assert.Equal(1, result[ShipmentStatus.OutForDelivery]);
    }

    [Fact]
    public async Task GetCurrentStatusCountsAsync_BranchRestricted_ExcludesShipmentTouchingNoAccessibleBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, originBranchId, destBranchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        dbContext.Add(BuildShipment(orgId, originBranchId, destBranchId, merchantId, ShipmentStatus.InTransit));
        await dbContext.SaveChangesAsync();

        var service = new ShipmentReportingLookupService(dbContext);
        var result = await service.GetCurrentStatusCountsAsync(new HashSet<Guid> { Guid.NewGuid() });

        Assert.False(result.ContainsKey(ShipmentStatus.InTransit));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~GetCurrentStatusCountsAsync"`
Expected: FAIL — `GetCurrentStatusCountsAsync` does not exist on `ShipmentReportingLookupService`.

- [ ] **Step 3: Add the method**

In `IShipmentReportingLookupService.cs`, add to the interface:

```csharp
    Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCurrentStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

In `ShipmentReportingLookupService.cs`, add:

```csharp
    // Live snapshot (no date range), unlike GetCohortStatusCountsAsync (cohort-by-creation-date,
    // used only by the org dashboard). Branch scoping reuses ShipmentService.Reads.cs:ListAsync's
    // exact predicate -- origin, destination, or current branch when non-null -- not a bare
    // CurrentBranchId filter, which would silently drop every in-transit/out-for-delivery/
    // RTO-in-transit shipment (CurrentBranchId is null exactly during those states).
    public async Task<IReadOnlyDictionary<ShipmentStatus, int>> GetCurrentStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Shipment>().AsNoTracking().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(s =>
                branchIdArray.Contains(s.OriginBranchId) ||
                branchIdArray.Contains(s.DestinationBranchId) ||
                (s.CurrentBranchId != null && branchIdArray.Contains(s.CurrentBranchId.Value)));
        }

        var counts = await query
            .GroupBy(s => s.CurrentStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Shipments.Tests --filter "FullyQualifiedName~GetCurrentStatusCountsAsync"`
Expected: PASS (3 tests)

- [ ] **Step 5: Write the failing API test**

Append to `tests/Api.Tests/ShipmentsEndpointsTests.cs`, inside its existing test class, reusing whichever org-registration helper it already has (mirrors `BranchesEndpointsTests.cs`'s `RegisterOrgAsync` pattern):

```csharp
    [Fact]
    public async Task GetShipmentsSummary_ReturnsStatusCounts_NotSwallowedByAwbRoute()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/shipments/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.NotNull(body); // empty org still returns a (possibly empty) object, not a 404 AWB lookup
    }

    [Fact]
    public async Task GetShipmentsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/shipments/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetShipmentsSummary"`
Expected: FAIL — 404 (no `/summary` route registered yet).

- [ ] **Step 7: Add the endpoint**

In `ShipmentsEndpoints.cs`, add inside `MapShipmentsEndpoints`, anywhere after `group` is declared (position doesn't matter for a literal segment — ASP.NET Core resolves `/summary` ahead of the unconstrained `/{awb}` parameter route by precedence, not registration order):

```csharp
        group.MapGet("/summary", async (ShipmentService service, IShipmentReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext, IPermissionChecker permissionChecker) =>
        {
            if (!await permissionChecker.HasPermissionAsync(httpContext.User, "shipment.read", httpContext.RequestAborted))
            {
                return Results.Forbid();
            }
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "shipment.read", httpContext.RequestAborted);
            var counts = await lookupService.GetCurrentStatusCountsAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(counts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        });
```

(`ShipmentService service` param is unused here and should be **omitted** — only include the services actually referenced: `IShipmentReportingLookupService`, `IBranchAuthorizationChecker`, `HttpContext`, `IPermissionChecker`. This route is staff-only, same as every other route in this file except the dual-auth ones — no `IMerchantAuthorizationChecker` needed.)

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetShipmentsSummary"`
Expected: PASS (2 tests)

- [ ] **Step 9: Commit backend**

```bash
git add src/Modules/Shipments tests/Shipments.Tests tests/Api.Tests
git commit -m "feat(shipments): add branch-scoped current-status-counts summary endpoint"
```

### Frontend

- [ ] **Step 10: Write the failing BFF route test**

```ts
// src/web/src/app/api/shipments/summary/route.test.ts
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/summary", () => {
  it("proxies to the backend summary endpoint", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ Created: 2, Delivered: 5 }),
    } as Response);

    const response = await GET(new NextRequest("http://localhost/api/shipments/summary"));

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/summary");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET(new NextRequest("http://localhost/api/shipments/summary"));

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 11: Run test to verify it fails**

Run: `npx vitest run src/app/api/shipments/summary/route.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 12: Write the route**

```ts
// src/web/src/app/api/shipments/summary/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/shipments/summary");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 13: Run test to verify it passes**

Run: `npx vitest run src/app/api/shipments/summary/route.test.ts`
Expected: PASS

- [ ] **Step 14: Write the failing hook test**

```tsx
// src/web/src/features/shipments/hooks/useShipmentsSummary.test.tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useShipmentsSummary } from "./useShipmentsSummary";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useShipmentsSummary", () => {
  it("fetches the summary proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ Created: 3, Delivered: 7 }),
    });

    const { result } = renderHook(() => useShipmentsSummary(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.Delivered).toBe(7);
    expect(global.fetch).toHaveBeenCalledWith("/api/shipments/summary", undefined);
  });
});
```

- [ ] **Step 15: Run test to verify it fails**

Run: `npx vitest run src/features/shipments/hooks/useShipmentsSummary.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 16: Write the hook**

```ts
// src/web/src/features/shipments/hooks/useShipmentsSummary.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useShipmentsSummary() {
  return useQuery({
    queryKey: ["shipments-summary"],
    queryFn: () => clientFetch<Record<string, number>>("/api/shipments/summary"),
  });
}
```

- [ ] **Step 17: Run test to verify it passes**

Run: `npx vitest run src/features/shipments/hooks/useShipmentsSummary.test.tsx`
Expected: PASS

- [ ] **Step 18: Wire the page**

Replace the full contents of `src/web/src/app/(staff)/shipments/page.tsx` with (adds the summary strip above the h1 row, restructures the early-return guards to wrap only the table, adds the tile-mapping function):

```tsx
"use client";

import { useState } from "react";
import { Plus, Package, Truck, Send, CheckCircle2, AlertTriangle, Undo2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useShipments } from "@/features/shipments/hooks/useShipments";
import { useShipmentsSummary } from "@/features/shipments/hooks/useShipmentsSummary";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import type { Shipment } from "@/features/shipments/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

// Bucket boundaries mirror components/status-badge/statusConfig.ts's own token groups exactly
// (not an independently invented split) so a tile's color always matches every StatusBadge of
// the statuses it counts, in the table directly below it.
function buildTiles(counts: Record<string, number>): StatTile[] {
  const sum = (...keys: string[]) => keys.reduce((total, key) => total + (counts[key] ?? 0), 0);
  return [
    { label: "Created", value: sum("Created", "PickupAssigned").toLocaleString(), icon: Package, badgeClass: "bg-status-created/15 text-status-created" },
    { label: "In Transit", value: sum("PickedUp", "AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch").toLocaleString(), icon: Truck, badgeClass: "bg-status-in-transit/15 text-status-in-transit" },
    { label: "Out for Delivery", value: sum("OutForDelivery").toLocaleString(), icon: Send, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
    { label: "Delivered", value: sum("Delivered").toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "NDR", value: sum("DeliveryFailed", "NdrCreated").toLocaleString(), icon: AlertTriangle, badgeClass: "bg-status-ndr/15 text-status-ndr" },
    { label: "RTO", value: sum("RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered").toLocaleString(), icon: Undo2, badgeClass: "bg-status-rto/15 text-status-rto" },
  ];
}

export default function ShipmentsPage() {
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useShipments(page, pageSize);
  const summary = useShipmentsSummary();
  const { data: serviceTypes } = useServiceTypes();
  const canCreate = usePermission("shipment.create");
  const openCreate = useShipmentPanelStore((s) => s.openCreate);
  const openDetail = useShipmentPanelStore((s) => s.openDetail);

  const serviceTypeName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

  const columns: ColumnDef<Shipment>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (s) => (
        <button
          type="button"
          onClick={() => openDetail(s.awb)}
          className="font-medium text-foreground hover:underline"
        >
          {s.awb}
        </button>
      ),
    },
    { key: "recipient", header: "Recipient", render: (s) => s.recipientName },
    { key: "status", header: "Status", render: (s) => <StatusBadge status={s.currentStatus} /> },
    { key: "serviceType", header: "Service Type", render: (s) => serviceTypeName(s.serviceTypeId) },
    { key: "cod", header: "COD Amount", render: (s) => currencyFormatter.format(s.codAmount) },
    {
      key: "createdAt",
      header: "Created At",
      render: (s) => new Date(s.createdAt).toLocaleDateString(),
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Shipments</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Shipment
          </Button>
        )}
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={6}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load shipments"
          description="Something went wrong fetching the shipment list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={data}
          isLoading={false}
          emptyTitle="No shipments yet"
          emptyDescription="Book your first shipment to get started."
          onPageChange={setPage}
          rowKey={(s) => s.id}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 19: Write a page-level test**

Create `src/web/src/app/(staff)/shipments/page.test.tsx` (no page-level test exists yet for this page):

```tsx
// src/web/src/app/(staff)/shipments/page.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import ShipmentsPage from "./page";

vi.mock("@/features/shipments/hooks/useShipments", () => ({
  useShipments: () => ({ data: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }, isLoading: false, isError: false, refetch: vi.fn() }),
}));
vi.mock("@/features/shipments/hooks/useShipmentsSummary", () => ({
  useShipmentsSummary: () => ({ data: { Created: 3, Delivered: 9 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({ useServiceTypes: () => ({ data: [] }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => false }));
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: { openCreate: () => void; openDetail: () => void }) => unknown) =>
    selector({ openCreate: vi.fn(), openDetail: vi.fn() }),
}));

function wrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={queryClient}>
      <ShipmentsPage />
    </QueryClientProvider>
  );
}

describe("ShipmentsPage", () => {
  it("renders the summary strip above the table using the summary hook's data", async () => {
    render(wrapper());
    await waitFor(() => expect(screen.getByText("Created")).toBeInTheDocument());
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("9")).toBeInTheDocument();
  });
});
```

- [ ] **Step 20: Run test to verify it passes**

Run: `npx vitest run "src/app/(staff)/shipments/page.test.tsx"`
Expected: PASS

- [ ] **Step 21: Commit frontend**

```bash
git add src/web/src/app/api/shipments/summary src/web/src/features/shipments/hooks/useShipmentsSummary.ts src/web/src/features/shipments/hooks/useShipmentsSummary.test.tsx "src/web/src/app/(staff)/shipments/page.tsx" "src/web/src/app/(staff)/shipments/page.test.tsx"
git commit -m "feat(web): show status-breakdown summary strip on Shipments page"
```

---

## Task 3: Branches Summary

**Files:**
- Modify: `src/Modules/Branches/Application/BranchDtos.cs`
- Modify: `src/Modules/Branches/Application/BranchService.cs`
- Modify: `src/Modules/Branches/Presentation/BranchesEndpoints.cs`
- Test: `tests/Branches.Tests/BranchServiceReportingTests.cs` (append)
- Test: `tests/Api.Tests/BranchesEndpointsTests.cs` (append)
- Create: `src/web/src/app/api/branches/summary/route.ts`
- Test: `src/web/src/app/api/branches/summary/route.test.ts`
- Create: `src/web/src/features/branches/hooks/useBranchesSummary.ts`
- Test: `src/web/src/features/branches/hooks/useBranchesSummary.test.tsx`
- Modify: `src/web/src/app/(staff)/branches/page.tsx`

**Interfaces:**
- Consumes: `SummaryStatsBar`/`StatTile` (Task 1).
- Produces: `GET /api/v1/branches/summary` → `BranchSummary { total: number; active: number; inactive: number; byType: Record<string, number> }` (camelCase — ASP.NET Core's default JSON casing, matching every other DTO in this codebase, e.g. `BranchResponse`).

### Backend

- [ ] **Step 1: Write the failing unit test**

Append to `tests/Branches.Tests/BranchServiceReportingTests.cs`, inside its existing `BranchServiceReportingTests` class (reusing its file-local `FixedTenantProvider`/`NoopAuditLogger`/`AllBranchesAuthorizationChecker`/`FakeBranchPlanLimitProvider` fakes and its `CreateSeededContext()` helper exactly as `GetNamesByIdsAsync_ReturnsNameForEachRequestedBranch` already does):

```csharp
    [Fact]
    public async Task GetSummaryAsync_CountsActiveInactiveAndByType()
    {
        await using var dbContext = CreateSeededContext();
        var organizationId = Guid.NewGuid();
        var activeHub = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "KTM-01", Name = "Kathmandu Hub",
            Type = BranchType.Hub, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 1, AddressLine = "New Road",
            DeliveryZoneId = null, IsActive = true
        };
        var inactiveBranch = new Branch
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Code = "KTM-02", Name = "Lalitpur Branch",
            Type = BranchType.Branch, ProvinceId = Guid.NewGuid(), DistrictId = Guid.NewGuid(),
            LocalLevelId = Guid.NewGuid(), WardNumber = 2, AddressLine = "Ring Road",
            DeliveryZoneId = null, IsActive = false
        };
        dbContext.Set<Branch>().Add(activeHub);
        dbContext.Set<Branch>().Add(inactiveBranch);
        await dbContext.SaveChangesAsync();

        var service = new BranchService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), new AllBranchesAuthorizationChecker(), new GeographyQueryService(dbContext), new FakeBranchPlanLimitProvider { MaxBranches = null });

        var result = await service.GetSummaryAsync(new ClaimsPrincipal());

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Active);
        Assert.Equal(1, result.Inactive);
        Assert.Equal(1, result.ByType[BranchType.Hub]);
        Assert.Equal(1, result.ByType[BranchType.Branch]);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Branches.Tests --filter "FullyQualifiedName~GetSummaryAsync"`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Add the method**

In `BranchDtos.cs`, add:

```csharp
public record BranchSummary(int Total, int Active, int Inactive, IReadOnlyDictionary<BranchType, int> ByType);
```

In `BranchService.cs`, add (near `ListBranchesAsync`, reusing its exact branch-id-resolution shape):

```csharp
    public async Task<BranchSummary> GetSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(user, "branch.read", cancellationToken);

        var query = _dbContext.Set<Branch>().AsNoTracking().AsQueryable();
        if (accessibleBranchIds is not null)
        {
            var ids = accessibleBranchIds.ToArray();
            query = query.Where(b => ids.Contains(b.Id));
        }

        var branches = await query.Select(b => new { b.IsActive, b.Type }).ToListAsync(cancellationToken);
        var byType = branches.GroupBy(b => b.Type).ToDictionary(g => g.Key, g => g.Count());
        return new BranchSummary(branches.Count, branches.Count(b => b.IsActive), branches.Count(b => !b.IsActive), byType);
    }
```

(Add `using System.Security.Claims;` to `BranchService.cs` if not already present — check first.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Branches.Tests --filter "FullyQualifiedName~GetSummaryAsync"`
Expected: PASS

- [ ] **Step 5: Write the failing API test**

Append to `tests/Api.Tests/BranchesEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetBranchesSummary_ReturnsCountsForCreatedBranches()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        await client.PostAsJsonAsync("/api/v1/branches", ValidBranchRequest("SUM-01"));

        var response = await client.GetAsync("/api/v1/branches/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task GetBranchesSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/branches/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetBranchesSummary"`
Expected: FAIL — 404.

- [ ] **Step 7: Add the endpoint**

In `BranchesEndpoints.cs`, add:

```csharp
        group.MapGet("/summary", async (BranchService branchService, HttpContext httpContext) =>
        {
            if (httpContext.User.GetUserId() is null)
            {
                return Results.Unauthorized();
            }
            var summary = await branchService.GetSummaryAsync(httpContext.User, httpContext.RequestAborted);
            return Results.Ok(summary);
        }).RequirePermission("branch.read");
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetBranchesSummary"`
Expected: PASS (2 tests)

- [ ] **Step 9: Commit backend**

```bash
git add src/Modules/Branches tests/Branches.Tests tests/Api.Tests
git commit -m "feat(branches): add branch-scoped summary endpoint"
```

### Frontend

- [ ] **Step 10-17: BFF route, hook, and their tests**

Follow Task 2's Steps 10-17 pattern exactly, substituting paths/names:

```ts
// src/web/src/app/api/branches/summary/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/branches/summary");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```ts
// src/web/src/features/branches/hooks/useBranchesSummary.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchType } from "@/features/branches/types";

export interface BranchSummary {
  total: number;
  active: number;
  inactive: number;
  byType: Record<BranchType, number>;
}

export function useBranchesSummary() {
  return useQuery({
    queryKey: ["branches-summary"],
    queryFn: () => clientFetch<BranchSummary>("/api/branches/summary"),
  });
}
```

Write `route.test.ts` and `useBranchesSummary.test.tsx` mirroring Task 2's Steps 10 and 14 verbatim structure (same mock shape, same assertions style, different fixture values: `{ total: 5, active: 4, inactive: 1, byType: { Hub: 2, Branch: 3 } }`).

- [ ] **Step 18: Wire the page**

Replace `src/web/src/app/(staff)/branches/page.tsx`:

```tsx
"use client";

import { Plus, Building2, CheckCircle2, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useBranchesSummary } from "@/features/branches/hooks/useBranchesSummary";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import type { Branch } from "@/features/branches/types";
import type { BranchSummary } from "@/features/branches/hooks/useBranchesSummary";

// Total/Active/Inactive only -- BranchType has no established color-token mapping the way
// shipment/manifest/NDR statuses do (BranchType isn't rendered via StatusBadge anywhere), so a
// per-type tile row would need 5 more neutral tiles with no color signal to differentiate them.
// byType stays in the API response for a future iteration rather than forcing that now (YAGNI).
function buildTiles(summary: BranchSummary): StatTile[] {
  return [
    { label: "Total Branches", value: summary.total.toLocaleString(), icon: Building2, badgeClass: "bg-muted text-muted-foreground" },
    { label: "Active", value: summary.active.toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "Inactive", value: summary.inactive.toLocaleString(), icon: XCircle, badgeClass: "bg-status-cancelled/15 text-status-cancelled" },
  ];
}

export default function BranchesPage() {
  const { data, isLoading, isError, refetch } = useBranches();
  const summary = useBranchesSummary();
  const canCreate = usePermission("branch.create");
  const openCreate = useBranchPanelStore((s) => s.openCreate);
  const openDetail = useBranchPanelStore((s) => s.openDetail);

  const columns: ColumnDef<Branch>[] = [
    { key: "code", header: "Code", render: (b) => b.code },
    {
      key: "name",
      header: "Name",
      render: (b) => (
        <button
          type="button"
          onClick={() => openDetail(b.id)}
          className="font-medium text-foreground hover:underline"
        >
          {b.name}
        </button>
      ),
    },
    { key: "type", header: "Type", render: (b) => b.type },
    { key: "active", header: "Active", render: (b) => (b.isActive ? "Yes" : "No") },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Branches</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Branch
          </Button>
        )}
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={3}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load branches"
          description="Something went wrong fetching the branch list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={
            data && {
              items: data,
              page: 1,
              pageSize: data.length,
              totalCount: data.length,
              totalPages: 1,
            }
          }
          isLoading={false}
          emptyTitle="No branches yet"
          emptyDescription="Create your organization's first branch to get started."
          onPageChange={() => {}}
          rowKey={(b) => b.id}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 19: Page-level test**

Mirror Task 2 Step 19, mocking `useBranches` and `useBranchesSummary`, asserting `"Total Branches"`/`"Active"`/`"Inactive"` labels render with mocked numeric values.

- [ ] **Step 20: Run all new frontend tests, verify pass**

Run: `npx vitest run src/app/api/branches/summary src/features/branches/hooks/useBranchesSummary "src/app/(staff)/branches/page.test.tsx"`

- [ ] **Step 21: Commit frontend**

```bash
git add src/web/src/app/api/branches/summary src/web/src/features/branches/hooks/useBranchesSummary.ts src/web/src/features/branches/hooks/useBranchesSummary.test.tsx "src/web/src/app/(staff)/branches/page.tsx" "src/web/src/app/(staff)/branches/page.test.tsx"
git commit -m "feat(web): show branch-count summary strip on Branches page"
```

---

## Task 4: Riders Summary

**Files:**
- Modify: `src/Modules/Riders/Application/IRiderReportingLookupService.cs`
- Modify: `src/Modules/Riders/Application/RiderReportingLookupService.cs`
- Modify: `src/Modules/Riders/Presentation/RidersEndpoints.cs`
- Test: `tests/Riders.Tests/RiderReportingLookupServiceTests.cs` (append)
- Test: `tests/Api.Tests/RidersEndpointsTests.cs` (append)
- Create: `src/web/src/app/api/riders/summary/route.ts`
- Test: `src/web/src/app/api/riders/summary/route.test.ts`
- Create: `src/web/src/features/riders/hooks/useRidersSummary.ts`
- Test: `src/web/src/features/riders/hooks/useRidersSummary.test.tsx`
- Modify: `src/web/src/app/(staff)/riders/page.tsx`

**Interfaces:**
- Consumes: `SummaryStatsBar`/`StatTile` (Task 1).
- Produces: `GET /api/v1/riders/summary` → `RiderSummary { total: number; active: number; inactive: number; currentlyAssigned: number }`.

### Backend

- [ ] **Step 1: Write the failing unit test**

Append to `tests/Riders.Tests/RiderReportingLookupServiceTests.cs`, inside the existing `RiderReportingLookupServiceTests` class (reusing its existing `FixedTenantProvider`/`SeedOrgAsync`/`BuildRider` helpers):

```csharp
    [Fact]
    public async Task GetSummaryAsync_CountsActiveInactiveAndCurrentlyAssigned()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var activeAssignedRider = BuildRider(orgId, branchId);
        var inactiveRider = BuildRider(orgId, branchId);
        inactiveRider.IsActive = false;
        dbContext.Add(activeAssignedRider);
        dbContext.Add(inactiveRider);
        var run = new DeliveryRun { Id = Guid.NewGuid(), RiderId = activeAssignedRider.Id, BranchId = branchId, Date = DateOnly.FromDateTime(DateTime.UtcNow), Status = DeliveryRunStatus.Active };
        dbContext.Add(run);
        dbContext.Add(new DeliveryTask { Id = Guid.NewGuid(), DeliveryRunId = run.Id, ShipmentId = Guid.NewGuid(), Type = DeliveryTaskType.Delivery, Sequence = 1, Status = DeliveryTaskStatus.Pending });
        await dbContext.SaveChangesAsync();

        var service = new RiderReportingLookupService(dbContext);
        var result = await service.GetSummaryAsync(new HashSet<Guid> { branchId });

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Active);
        Assert.Equal(1, result.Inactive);
        Assert.Equal(1, result.CurrentlyAssigned);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Riders.Tests --filter "FullyQualifiedName~RiderReportingLookupServiceTests.GetSummaryAsync"`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Add the method**

In `IRiderReportingLookupService.cs`, add:

```csharp
public record RiderSummary(int Total, int Active, int Inactive, int CurrentlyAssigned);
```
and to the interface:
```csharp
    Task<RiderSummary> GetSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

In `RiderReportingLookupService.cs`, add (reusing the exact `DeliveryTask`/`DeliveryRun` join `GetBranchRiderTaskLoadAsync` already uses, generalized to a branch set instead of one branch):

```csharp
    public async Task<RiderSummary> GetSummaryAsync(IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Rider>().AsNoTracking().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(r => branchIdArray.Contains(r.BaseBranchId));
        }

        var riders = await query.Select(r => new { r.Id, r.IsActive }).ToListAsync(cancellationToken);
        var riderIds = riders.Select(r => r.Id).ToArray();

        var currentlyAssigned = await (
            from task in _dbContext.Set<DeliveryTask>().AsNoTracking()
            join run in _dbContext.Set<DeliveryRun>().AsNoTracking() on task.DeliveryRunId equals run.Id
            where task.Status == DeliveryTaskStatus.Pending && riderIds.Contains(run.RiderId)
            select run.RiderId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new RiderSummary(riders.Count, riders.Count(r => r.IsActive), riders.Count(r => !r.IsActive), currentlyAssigned);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Riders.Tests --filter "FullyQualifiedName~RiderReportingLookupServiceTests.GetSummaryAsync"`
Expected: PASS

- [ ] **Step 5: Write the failing API test**

Append to `tests/Api.Tests/RidersEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetRidersSummary_ReturnsCounts()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/riders/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetRidersSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/riders/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetRidersSummary"`
Expected: FAIL — 404.

- [ ] **Step 7: Add the endpoint**

In `RidersEndpoints.cs`, add (inside the `riders` group):

```csharp
        riders.MapGet("/summary", async (IRiderReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext) =>
        {
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "rider.read", httpContext.RequestAborted);
            var summary = await lookupService.GetSummaryAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(summary);
        }).RequirePermission("rider.read");
```

(Add `using Courier.SharedKernel;` if not already present in this file for `IBranchAuthorizationChecker` — it already is, via the existing `RequirePermission` extension's namespace; verify with the file's current `using` list.)

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetRidersSummary"`
Expected: PASS (2 tests)

- [ ] **Step 9: Commit backend**

```bash
git add src/Modules/Riders tests/Riders.Tests tests/Api.Tests
git commit -m "feat(riders): add branch-scoped summary endpoint"
```

### Frontend

- [ ] **Step 10-17: BFF route, hook, and their tests**

```ts
// src/web/src/app/api/riders/summary/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/riders/summary");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```ts
// src/web/src/features/riders/hooks/useRidersSummary.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface RiderSummary {
  total: number;
  active: number;
  inactive: number;
  currentlyAssigned: number;
}

export function useRidersSummary() {
  return useQuery({
    queryKey: ["riders-summary"],
    queryFn: () => clientFetch<RiderSummary>("/api/riders/summary"),
  });
}
```

Write `route.test.ts` and `useRidersSummary.test.tsx` mirroring Task 2 Steps 10/14 exactly (fixture: `{ total: 6, active: 5, inactive: 1, currentlyAssigned: 2 }`).

- [ ] **Step 18: Wire the page**

Replace the full contents of `src/web/src/app/(staff)/riders/page.tsx`:

```tsx
"use client";

import { Plus, Bike, CheckCircle2, XCircle, ClipboardCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useVehicles } from "@/features/riders/hooks/useVehicles";
import { useRidersSummary, type RiderSummary } from "@/features/riders/hooks/useRidersSummary";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import type { Rider, Vehicle } from "@/features/riders/types";

function buildTiles(summary: RiderSummary): StatTile[] {
  return [
    { label: "Total Riders", value: summary.total.toLocaleString(), icon: Bike, badgeClass: "bg-muted text-muted-foreground" },
    { label: "Active", value: summary.active.toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "Inactive", value: summary.inactive.toLocaleString(), icon: XCircle, badgeClass: "bg-status-cancelled/15 text-status-cancelled" },
    { label: "Currently Assigned", value: summary.currentlyAssigned.toLocaleString(), icon: ClipboardCheck, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
  ];
}

export default function RidersPage() {
  const { data, isLoading, isError, refetch } = useRiders();
  const summary = useRidersSummary();
  const canCreate = usePermission("rider.create");
  const openCreateRider = useRiderPanelStore((s) => s.openCreateRider);
  const { data: vehicles, isLoading: vehiclesLoading } = useVehicles();
  const openCreateVehicle = useRiderPanelStore((s) => s.openCreateVehicle);
  const canAssign = usePermission("delivery.assign");
  const openAssignTask = useRiderPanelStore((s) => s.openAssignTask);

  const columns: ColumnDef<Rider>[] = [
    { key: "licenseNumber", header: "License Number", render: (r) => r.licenseNumber },
    { key: "roleType", header: "Role", render: (r) => r.roleType },
    { key: "active", header: "Active", render: (r) => (r.isActive ? "Yes" : "No") },
  ];

  const riderLicenseById = new Map((data ?? []).map((r) => [r.id, r.licenseNumber]));

  const vehicleColumns: ColumnDef<Vehicle>[] = [
    { key: "plateNumber", header: "Plate Number", render: (v) => v.plateNumber },
    { key: "type", header: "Type", render: (v) => v.type },
    {
      key: "riderId",
      header: "Assigned Rider",
      render: (v) => (v.riderId ? (riderLicenseById.get(v.riderId) ?? v.riderId) : "Unassigned"),
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Riders</h1>
        <div className="flex items-center gap-2">
          {canAssign && (
            <Button size="sm" variant="outline" className="gap-1.5" onClick={() => openAssignTask()}>
              Assign Task
            </Button>
          )}
          {canCreate && (
            <Button size="sm" className="gap-1.5" onClick={() => openCreateRider()}>
              <Plus className="h-4 w-4" strokeWidth={2} />
              New Rider
            </Button>
          )}
        </div>
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={4}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load riders"
          description="Something went wrong fetching the rider list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <>
          <DataTable
            columns={columns}
            data={
              data && {
                items: data,
                page: 1,
                pageSize: data.length,
                totalCount: data.length,
                totalPages: 1,
              }
            }
            isLoading={false}
            emptyTitle="No riders yet"
            emptyDescription="Create your first rider to start assigning pickups and deliveries."
            onPageChange={() => {}}
            rowKey={(r) => r.id}
          />
          <div className="flex items-center justify-between pt-4">
            <h2 className="font-display text-lg font-semibold">Vehicles</h2>
            {canCreate && (
              <Button size="sm" variant="outline" className="gap-1.5" onClick={() => openCreateVehicle()}>
                <Plus className="h-4 w-4" strokeWidth={2} />
                New Vehicle
              </Button>
            )}
          </div>
          <DataTable
            columns={vehicleColumns}
            data={
              vehicles && {
                items: vehicles,
                page: 1,
                pageSize: vehicles.length,
                totalCount: vehicles.length,
                totalPages: 1,
              }
            }
            isLoading={vehiclesLoading}
            emptyTitle="No vehicles yet"
            emptyDescription="Add a vehicle to assign it to a rider."
            onPageChange={() => {}}
            rowKey={(v) => v.id}
          />
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 19: Page-level test**

Mirror Task 2 Step 19: mock `useRiders`, `useVehicles`, `useRidersSummary`, `usePermission`, `useRiderPanelStore`; assert `"Total Riders"` and its mocked value render.

- [ ] **Step 20: Run all new frontend tests, verify pass**

Run: `npx vitest run src/app/api/riders/summary src/features/riders/hooks/useRidersSummary "src/app/(staff)/riders/page.test.tsx"`

- [ ] **Step 21: Commit frontend**

```bash
git add src/web/src/app/api/riders/summary src/web/src/features/riders/hooks/useRidersSummary.ts src/web/src/features/riders/hooks/useRidersSummary.test.tsx "src/web/src/app/(staff)/riders/page.tsx" "src/web/src/app/(staff)/riders/page.test.tsx"
git commit -m "feat(web): show rider-count summary strip on Riders page"
```

---

## Task 5: Manifests Summary

**Files:**
- Modify: `src/Modules/Manifests/Application/IManifestReportingLookupService.cs`
- Modify: `src/Modules/Manifests/Application/ManifestReportingLookupService.cs`
- Modify: `src/Modules/Manifests/Presentation/ManifestsEndpoints.cs`
- Test: `tests/Manifests.Tests/ManifestReportingLookupServiceTests.cs` (append)
- Test: `tests/Api.Tests/ManifestsEndpointsTests.cs` (append)
- Create: `src/web/src/app/api/manifests/summary/route.ts`
- Test: `src/web/src/app/api/manifests/summary/route.test.ts`
- Create: `src/web/src/features/manifests/hooks/useManifestsSummary.ts`
- Test: `src/web/src/features/manifests/hooks/useManifestsSummary.test.tsx`
- Modify: `src/web/src/app/(staff)/manifests/page.tsx`

**Interfaces:**
- Consumes: `SummaryStatsBar`/`StatTile` (Task 1).
- Produces: `GET /api/v1/manifests/summary` → `Record<ManifestStatus, number>` (`{"Draft": 2, "Sealed": 1, ...}`).

### Backend

- [ ] **Step 1: Write the failing unit test**

Append to `tests/Manifests.Tests/ManifestReportingLookupServiceTests.cs`, inside its existing `ManifestReportingLookupServiceTests` class (reusing its existing `FixedTenantProvider`/`SeedOrgAsync` helpers, which already return `(OrgId, FromBranchId, ToBranchId)`):

```csharp
    [Fact]
    public async Task GetStatusCountsAsync_GroupsByStatus_RespectsFromOrToBranchFilter()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, fromBranchId, toBranchId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        // draftAsSender: fromBranchId is the seeded branch (sender-side match).
        // inTransitAsReceiver: toBranchId is the seeded branch (receiver-side match) -- both
        // should count for a caller restricted to fromBranchId, matching
        // ManifestService.Reads.cs:ListAsync's own FromBranchId-OR-ToBranchId convention.
        var draftAsSender = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF001", FromBranchId = fromBranchId, ToBranchId = toBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.Draft, ShipmentCount = 0, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var inTransitAsReceiver = new Manifest { Id = Guid.NewGuid(), OrganizationId = orgId, Code = "MNF002", FromBranchId = Guid.NewGuid(), ToBranchId = fromBranchId, Purpose = ManifestPurpose.Forward, Status = ManifestStatus.InTransit, ShipmentCount = 1, CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        dbContext.Add(draftAsSender);
        dbContext.Add(inTransitAsReceiver);
        await dbContext.SaveChangesAsync();

        var service = new ManifestReportingLookupService(dbContext);
        var result = await service.GetStatusCountsAsync(new HashSet<Guid> { fromBranchId });

        Assert.Equal(1, result[ManifestStatus.Draft]);
        Assert.Equal(1, result[ManifestStatus.InTransit]);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Manifests.Tests --filter "FullyQualifiedName~GetStatusCountsAsync"`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Add the method**

In `IManifestReportingLookupService.cs`:

```csharp
    Task<IReadOnlyDictionary<ManifestStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

In `ManifestReportingLookupService.cs`:

```csharp
    public async Task<IReadOnlyDictionary<ManifestStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Set<Manifest>().AsNoTracking().AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(m => branchIdArray.Contains(m.FromBranchId) || branchIdArray.Contains(m.ToBranchId));
        }

        var counts = await query
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Manifests.Tests --filter "FullyQualifiedName~GetStatusCountsAsync"`
Expected: PASS

- [ ] **Step 5: Write the failing API test**

Append to `tests/Api.Tests/ManifestsEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetManifestsSummary_ReturnsStatusCounts()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/manifests/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetManifestsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/manifests/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetManifestsSummary"`
Expected: FAIL — 404.

- [ ] **Step 7: Add the endpoint**

In `ManifestsEndpoints.cs`, add:

```csharp
        group.MapGet("/summary", async (IManifestReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext) =>
        {
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "manifest.read", httpContext.RequestAborted);
            var counts = await lookupService.GetStatusCountsAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(counts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        }).RequirePermission("manifest.read");
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetManifestsSummary"`
Expected: PASS (2 tests)

- [ ] **Step 9: Commit backend**

```bash
git add src/Modules/Manifests tests/Manifests.Tests tests/Api.Tests
git commit -m "feat(manifests): add branch-scoped status-counts summary endpoint"
```

### Frontend

- [ ] **Step 10-17: BFF route, hook, and their tests**

```ts
// src/web/src/app/api/manifests/summary/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/manifests/summary");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```ts
// src/web/src/features/manifests/hooks/useManifestsSummary.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useManifestsSummary() {
  return useQuery({
    queryKey: ["manifests-summary"],
    queryFn: () => clientFetch<Record<string, number>>("/api/manifests/summary"),
  });
}
```

Write `route.test.ts` / `useManifestsSummary.test.tsx` mirroring Task 2 Steps 10/14 (fixture: `{ Draft: 1, Sealed: 2, InTransit: 3, Received: 1, Closed: 4 }`).

- [ ] **Step 18: Wire the page**

Replace the full contents of `src/web/src/app/(staff)/manifests/page.tsx`:

```tsx
"use client";

import { Plus, FileText, Lock, Truck, PackageCheck, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUiStore } from "@/lib/store/uiStore";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useManifests } from "@/features/manifests/hooks/useManifests";
import { useManifestsSummary } from "@/features/manifests/hooks/useManifestsSummary";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import type { Manifest } from "@/features/manifests/types";

function buildTiles(counts: Record<string, number>): StatTile[] {
  const value = (key: string) => (counts[key] ?? 0).toLocaleString();
  return [
    { label: "Draft", value: value("Draft"), icon: FileText, badgeClass: "bg-status-created/15 text-status-created" },
    { label: "Sealed", value: value("Sealed"), icon: Lock, badgeClass: "bg-status-sealed/15 text-status-sealed" },
    { label: "In Transit", value: value("InTransit"), icon: Truck, badgeClass: "bg-status-in-transit/15 text-status-in-transit" },
    { label: "Received", value: value("Received"), icon: PackageCheck, badgeClass: "bg-status-received/15 text-status-received" },
    { label: "Closed", value: value("Closed"), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered" },
  ];
}

export default function ManifestsPage() {
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const { data, isLoading, isError, refetch } = useManifests(activeBranchId ?? undefined);
  const summary = useManifestsSummary();
  const { data: branches } = useBranches();
  const canCreate = usePermission("manifest.create");
  const openCreate = useManifestPanelStore((s) => s.openCreate);
  const openDetail = useManifestPanelStore((s) => s.openDetail);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";

  const columns: ColumnDef<Manifest>[] = [
    {
      key: "code",
      header: "Code",
      render: (m) => (
        <button
          type="button"
          onClick={() => openDetail(m.id)}
          className="font-medium text-foreground hover:underline"
        >
          {m.code}
        </button>
      ),
    },
    { key: "fromBranch", header: "From Branch", render: (m) => branchName(m.fromBranchId) },
    { key: "toBranch", header: "To Branch", render: (m) => branchName(m.toBranchId) },
    { key: "purpose", header: "Purpose", render: (m) => m.purpose },
    {
      key: "status",
      header: "Status",
      render: (m) => <StatusBadge status={m.status} namespace="Manifest" />,
    },
    { key: "shipmentCount", header: "Shipments", render: (m) => String(m.shipmentCount) },
    {
      key: "createdAt",
      header: "Created At",
      render: (m) => new Date(m.createdAt).toLocaleDateString(),
    },
  ];

  const pagedData: PagedResult<Manifest> | undefined = data && {
    items: data,
    page: 1,
    pageSize: data.length,
    totalCount: data.length,
    totalPages: 1,
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Manifests</h1>
        {canCreate && (
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            New Manifest
          </Button>
        )}
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={5}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load manifests"
          description="Something went wrong fetching the manifest list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={pagedData}
          isLoading={false}
          emptyTitle="No manifests yet"
          emptyDescription="Create a manifest to start building an outgoing transfer."
          onPageChange={() => {}}
          rowKey={(m) => m.id}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 19: Page-level test**

Mirror Task 2 Step 19: mock `useManifests`, `useBranches`, `useManifestsSummary`, `usePermission`, `useManifestPanelStore`; assert `"Draft"`/`"Sealed"`/etc. labels render with mocked values.

- [ ] **Step 20: Run all new frontend tests, verify pass**

Run: `npx vitest run src/app/api/manifests/summary src/features/manifests/hooks/useManifestsSummary "src/app/(staff)/manifests/page.test.tsx"`

- [ ] **Step 21: Commit frontend**

```bash
git add src/web/src/app/api/manifests/summary src/web/src/features/manifests/hooks/useManifestsSummary.ts src/web/src/features/manifests/hooks/useManifestsSummary.test.tsx "src/web/src/app/(staff)/manifests/page.tsx" "src/web/src/app/(staff)/manifests/page.test.tsx"
git commit -m "feat(web): show status-breakdown summary strip on Manifests page"
```

---

## Task 6: NDRs Summary

**Files:**
- Modify: `src/Modules/Ndr/Application/INdrReportingLookupService.cs`
- Modify: `src/Modules/Ndr/Application/NdrReportingLookupService.cs`
- Modify: `src/Modules/Ndr/Presentation/NdrEndpoints.cs`
- Test: `tests/Ndr.Tests/NdrReportingLookupServiceTests.cs` (append)
- Test: `tests/Api.Tests/NdrEndpointsTests.cs` (append)
- Create: `src/web/src/app/api/ndrs/summary/route.ts`
- Test: `src/web/src/app/api/ndrs/summary/route.test.ts`
- Create: `src/web/src/features/ndr/hooks/useNdrsSummary.ts`
- Test: `src/web/src/features/ndr/hooks/useNdrsSummary.test.tsx`
- Modify: `src/web/src/app/(staff)/ndrs/page.tsx`

**Interfaces:**
- Consumes: `SummaryStatsBar`/`StatTile` (Task 1).
- Produces: `GET /api/v1/ndrs/summary` → `Record<NdrStatus, number>`.

### Backend

- [ ] **Step 1: Write the failing unit test**

Append to `tests/Ndr.Tests/NdrReportingLookupServiceTests.cs`, inside its existing `NdrReportingLookupServiceTests` class (reusing its existing `FixedTenantProvider`/`SeedOrgAsync`/`BuildShipment` helpers — `BuildShipment` sets `CurrentStatus = ShipmentStatus.NdrCreated` and both branch fields to the same seeded `branchId`, so build the out-of-scope row via its own object initializer with a different `DestinationBranchId`, same shape `BuildShipment` itself uses):

```csharp
    [Fact]
    public async Task GetStatusCountsAsync_GroupsByStatus_ScopedByShipmentDestinationBranch()
    {
        var tenantProvider = new FixedTenantProvider();
        await using var dbContext = TestDbContextFactory.Create(tenantProvider);
        var (orgId, branchId, merchantId) = await SeedOrgAsync(dbContext);
        tenantProvider.OrganizationId = orgId;

        var otherBranchId = Guid.NewGuid();
        var inScopeShipment = BuildShipment(orgId, branchId, merchantId);
        var outOfScopeShipment = BuildShipment(orgId, branchId, merchantId);
        outOfScopeShipment.DestinationBranchId = otherBranchId;
        dbContext.Add(inScopeShipment);
        dbContext.Add(outOfScopeShipment);
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = inScopeShipment.Id, Reason = NdrReason.CustomerUnavailable, Status = NdrStatus.Open });
        dbContext.Add(new NDR { Id = Guid.NewGuid(), OrganizationId = orgId, ShipmentId = outOfScopeShipment.Id, Reason = NdrReason.WrongAddress, Status = NdrStatus.Contacting });
        await dbContext.SaveChangesAsync();

        var service = new NdrReportingLookupService(dbContext);
        var result = await service.GetStatusCountsAsync(new HashSet<Guid> { branchId });

        Assert.Equal(1, result[NdrStatus.Open]);
        Assert.False(result.ContainsKey(NdrStatus.Contacting));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Ndr.Tests --filter "FullyQualifiedName~NdrReportingLookupServiceTests.GetStatusCountsAsync"`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Add the method**

In `INdrReportingLookupService.cs`:

```csharp
    Task<IReadOnlyDictionary<NdrStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default);
```

In `NdrReportingLookupService.cs` (reusing the exact `Join` shape `GetOpenNdrCountForBranchAsync` already uses — pushed into SQL, no per-row loop, no new dependency):

```csharp
    public async Task<IReadOnlyDictionary<NdrStatus, int>> GetStatusCountsAsync(
        IReadOnlySet<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var joined = _dbContext.Set<NDR>().AsNoTracking()
            .Join(_dbContext.Set<Shipment>().AsNoTracking(),
                n => n.ShipmentId, s => s.Id, (n, s) => new { n.Status, s.DestinationBranchId });

        var query = joined.AsQueryable();
        if (branchIds is not null)
        {
            var branchIdArray = branchIds.ToArray();
            query = query.Where(x => branchIdArray.Contains(x.DestinationBranchId));
        }

        var counts = await query
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.Status, x => x.Count);
    }
```

(`branchIdArray.Contains(...)` — a materialized array, not the `IReadOnlySet<Guid>` directly — inside the `Where`, matching every other new method in this plan; EF Core's SQL translator needs that shape, same as `BranchService.cs`'s existing `ListBranchesAsync` already does with `accessibleBranchIds.ToArray()`.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Ndr.Tests --filter "FullyQualifiedName~NdrReportingLookupServiceTests.GetStatusCountsAsync"`
Expected: PASS

- [ ] **Step 5: Write the failing NDR summary API test**

Append to `tests/Api.Tests/NdrEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetNdrsSummary_ReturnsStatusCounts()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/ndrs/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetNdrsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/ndrs/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetNdrsSummary"`
Expected: FAIL — 404.

- [ ] **Step 7: Add the endpoint**

In `NdrEndpoints.cs`, add inside the `group` (`/api/v1/ndrs`) block:

```csharp
        group.MapGet("/summary", async (INdrReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext) =>
        {
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "ndr.read", httpContext.RequestAborted);
            var counts = await lookupService.GetStatusCountsAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(counts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        }).RequirePermission("ndr.read");
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetNdrsSummary"`
Expected: PASS (2 tests)

- [ ] **Step 9: Commit backend**

```bash
git add src/Modules/Ndr tests/Ndr.Tests tests/Api.Tests
git commit -m "feat(ndr): add branch-scoped status-counts summary endpoint"
```

### Frontend

- [ ] **Step 10-17: BFF route, hook, and their tests**

```ts
// src/web/src/app/api/ndrs/summary/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/ndrs/summary");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```ts
// src/web/src/features/ndr/hooks/useNdrsSummary.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useNdrsSummary() {
  return useQuery({
    queryKey: ["ndrs-summary"],
    queryFn: () => clientFetch<Record<string, number>>("/api/ndrs/summary"),
  });
}
```

Write `route.test.ts` / `useNdrsSummary.test.tsx` mirroring Task 2 Steps 10/14 (fixture: `{ Open: 4, Contacting: 2, CustomerConfirmed: 1, ReattemptScheduled: 1, RtoRequested: 1, Resolved: 6 }`).

- [ ] **Step 18: Wire the page**

Replace the full contents of `src/web/src/app/(staff)/ndrs/page.tsx`:

```tsx
"use client";

import { useState } from "react";
import { AlertTriangle, Phone, CheckCircle2, RotateCcw, Undo2 } from "lucide-react";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { useSession } from "@/features/auth/hooks/useSession";
import { useNdrs } from "@/features/ndr/hooks/useNdrs";
import { useNdrsSummary } from "@/features/ndr/hooks/useNdrsSummary";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import type { Ndr, NdrStatus } from "@/features/ndr/types";

const STATUS_OPTIONS: NdrStatus[] = ["Open", "Contacting", "CustomerConfirmed", "ReattemptScheduled", "RtoRequested", "Resolved"];

function buildTiles(counts: Record<string, number>): StatTile[] {
  const value = (key: string) => (counts[key] ?? 0).toLocaleString();
  return [
    { label: "Open", value: value("Open"), icon: AlertTriangle, badgeClass: "bg-status-ndr/15 text-status-ndr" },
    { label: "Contacting", value: value("Contacting"), icon: Phone, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
    { label: "Customer Confirmed", value: value("CustomerConfirmed"), icon: CheckCircle2, badgeClass: "bg-status-received/15 text-status-received" },
    { label: "Reattempt Scheduled", value: value("ReattemptScheduled"), icon: RotateCcw, badgeClass: "bg-status-sealed/15 text-status-sealed" },
    { label: "RTO Requested", value: value("RtoRequested"), icon: Undo2, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "Resolved", value: value("Resolved"), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
  ];
}

export default function NdrsPage() {
  const [status, setStatus] = useState<NdrStatus | "all">("all");
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useNdrs({
    status: status === "all" ? undefined : status,
    page,
    pageSize,
  });
  const summary = useNdrsSummary();
  const openDetail = useNdrPanelStore((s) => s.openDetail);
  const { data: session } = useSession();

  const columns: ColumnDef<Ndr>[] = [
    {
      key: "awb",
      header: "AWB",
      render: (n) => (
        <button type="button" onClick={() => openDetail(n.id)} className="font-medium text-foreground hover:underline">
          {n.awb}
        </button>
      ),
    },
    { key: "reason", header: "Reason", render: (n) => n.reason },
    { key: "status", header: "Status", render: (n) => <StatusBadge status={n.status} namespace="Ndr" /> },
    { key: "createdAt", header: "Created At", render: (n) => new Date(n.createdAt).toLocaleDateString() },
    {
      key: "assignedStaff",
      header: "Assigned Staff",
      render: (n) =>
        !n.assignedStaffUserId ? "—" : n.assignedStaffUserId === session?.userId ? "You" : "Assigned",
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">NDRs</h1>
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v as NdrStatus | "all");
            setPage(1);
          }}
        >
          <SelectTrigger className="w-48" aria-label="Filter by status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            {STATUS_OPTIONS.map((s) => (
              <SelectItem key={s} value={s}>{s}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={6}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load NDRs"
          description="Something went wrong fetching the NDR queue."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={data}
          isLoading={false}
          emptyTitle="No NDRs"
          emptyDescription="Failed deliveries that need follow-up will show up here."
          onPageChange={setPage}
          rowKey={(n) => n.id}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 19: Page-level test**

Mirror Task 2 Step 19: mock `useNdrs`, `useSession`, `useNdrsSummary`, `useNdrPanelStore`; assert `"Open"`/`"Resolved"` labels render with mocked values.

- [ ] **Step 20: Run all new frontend tests, verify pass**

Run: `npx vitest run src/app/api/ndrs/summary src/features/ndr/hooks/useNdrsSummary "src/app/(staff)/ndrs/page.test.tsx"`

- [ ] **Step 21: Commit frontend**

```bash
git add src/web/src/app/api/ndrs/summary src/web/src/features/ndr/hooks/useNdrsSummary.ts src/web/src/features/ndr/hooks/useNdrsSummary.test.tsx "src/web/src/app/(staff)/ndrs/page.tsx" "src/web/src/app/(staff)/ndrs/page.test.tsx"
git commit -m "feat(web): show status-breakdown summary strip on NDRs page"
```

---

## Task 7: Return Shipments Summary

**Files:**
- Create: `src/Modules/Ndr/Application/NdrService.Summary.cs`
- Modify: `src/Modules/Ndr/Presentation/NdrEndpoints.cs`
- Test: `tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs` (append)
- Test: `tests/Api.Tests/NdrEndpointsTests.cs` (append)
- Create: `src/web/src/app/api/return-shipments/summary/route.ts`
- Test: `src/web/src/app/api/return-shipments/summary/route.test.ts`
- Create: `src/web/src/features/ndr/hooks/useReturnShipmentsSummary.ts`
- Test: `src/web/src/features/ndr/hooks/useReturnShipmentsSummary.test.tsx`
- Modify: `src/web/src/app/(staff)/return-shipments/page.tsx`

**Interfaces:**
- Consumes: `SummaryStatsBar`/`StatTile` (Task 1).
- Produces: `GET /api/v1/return-shipments/summary` → `ReturnShipmentSummary { byRtoStage: Record<string, number>; merchantConfirmed: number; pendingConfirmation: number }`.

This one differs from Tasks 2/4/5/6: it can't live on a `*ReportingLookupService` (those classes take only `DbContext`, no `IShipmentTransitionService`, and existing unit tests construct them with that single-argument constructor directly — adding a second constructor argument would break every one of those tests). `ReturnShipment` doesn't store `CurrentStatus` (`ReturnShipment.cs`'s own doc comment: the underlying `Shipment` is the single source of truth), so grouping by RTO stage needs the same per-row `IShipmentTransitionService.GetScanInfoAsync` lookup `NdrService.ReturnShipments.cs:ListReturnShipmentsAsync` already pays for every row — and `NdrService` already has that dependency injected. So this method lives on `NdrService` itself, in a new partial-class file, following the same one-new-file-per-concern convention as its siblings (`NdrService.Reads.cs`, `NdrService.Resolve.cs`, `NdrService.ReturnShipments.cs`).

### Backend

- [ ] **Step 1: Write the failing unit test**

Append to `tests/Ndr.Tests/NdrServiceReturnShipmentTests.cs`, inside its existing `NdrServiceReturnShipmentTests` class (reusing its file-local `FixedTenantProvider`/`NoopAuditLogger`/`AllBranchesAuthorizationChecker`/`FakeShipmentTransitionService` fakes, its `CreateContext()` helper, and its `ActingUser(Guid)` helper — do not reuse `SeedReturnShipmentAsync` as-is here, since it constructs its own throwaway `NdrService`/`FakeShipmentTransitionService` per call and this test needs both seeded rows visible to one shared instance of each):

```csharp
    [Fact]
    public async Task GetReturnShipmentSummaryAsync_GroupsByRtoStageAndConfirmationState()
    {
        await using var dbContext = CreateContext();
        var organizationId = Guid.NewGuid();
        var confirmedOriginalId = Guid.NewGuid();
        var pendingOriginalId = Guid.NewGuid();
        var originBranchId = Guid.NewGuid();
        var destinationBranchId = Guid.NewGuid();

        var confirmed = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = confirmedOriginalId, OrganizationId = organizationId,
            ReturnAwb = "RTO-001", RtoReason = "Customer refused",
            OriginBranchId = destinationBranchId, DestinationBranchId = originBranchId,
            MerchantConfirmedAt = DateTimeOffset.UtcNow
        };
        var pending = new ReturnShipment
        {
            Id = Guid.NewGuid(), OriginalShipmentId = pendingOriginalId, OrganizationId = organizationId,
            ReturnAwb = "RTO-002", RtoReason = "Wrong address",
            OriginBranchId = destinationBranchId, DestinationBranchId = originBranchId
        };
        dbContext.Set<ReturnShipment>().Add(confirmed);
        dbContext.Set<ReturnShipment>().Add(pending);
        await dbContext.SaveChangesAsync();

        var shipments = new FakeShipmentTransitionService();
        shipments.ShipmentsById[confirmedOriginalId] = new ShipmentScanInfo(confirmedOriginalId, organizationId, "AWB001", ShipmentStatus.RtoAtOrigin, originBranchId, originBranchId, destinationBranchId, null);
        shipments.ShipmentsById[pendingOriginalId] = new ShipmentScanInfo(pendingOriginalId, organizationId, "AWB002", ShipmentStatus.RtoInTransit, null, originBranchId, destinationBranchId, null);
        var service = new NdrService(dbContext, new FixedTenantProvider(organizationId), new NoopAuditLogger(), shipments, new AllBranchesAuthorizationChecker());

        var result = await service.GetReturnShipmentSummaryAsync(ActingUser(Guid.NewGuid()));

        Assert.Equal(1, result.ByRtoStage[ShipmentStatus.RtoAtOrigin]);
        Assert.Equal(1, result.ByRtoStage[ShipmentStatus.RtoInTransit]);
        Assert.Equal(1, result.MerchantConfirmed);
        Assert.Equal(1, result.PendingConfirmation);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Ndr.Tests --filter "FullyQualifiedName~GetReturnShipmentSummaryAsync"`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Add the method**

Create `src/Modules/Ndr/Application/NdrService.Summary.cs`:

```csharp
using System.Security.Claims;
using Courier.Ndr.Domain;
using Courier.Shipments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Courier.Ndr.Application;

public record ReturnShipmentSummary(
    IReadOnlyDictionary<ShipmentStatus, int> ByRtoStage, int MerchantConfirmed, int PendingConfirmation);

public partial class NdrService
{
    // Scoped the same way ListReturnShipmentsAsync already is: ReturnShipment stores
    // DestinationBranchId as a real column (despite the name, per ReturnShipment.cs's own doc
    // comment this is where the parcel ends up after RtoAtOrigin -- the original shipment's
    // origin branch), so the filter pushes straight into SQL, no join needed for scoping.
    // CurrentStatus isn't stored on ReturnShipment itself though, so grouping by RTO stage still
    // needs the same per-row GetScanInfoAsync lookup ListReturnShipmentsAsync already pays for
    // every row it returns -- no new performance characteristic, just reused here.
    public async Task<ReturnShipmentSummary> GetReturnShipmentSummaryAsync(ClaimsPrincipal actingUser, CancellationToken cancellationToken = default)
    {
        var accessibleBranchIds = await _branchAuthorizationChecker.GetAccessibleBranchIdsAsync(actingUser, "rto.read", cancellationToken);

        var query = _dbContext.Set<ReturnShipment>().AsNoTracking().AsQueryable();
        if (accessibleBranchIds is not null)
        {
            var branchIdArray = accessibleBranchIds.ToArray();
            query = query.Where(r => branchIdArray.Contains(r.DestinationBranchId));
        }

        var returnShipments = await query.ToListAsync(cancellationToken);
        var merchantConfirmed = returnShipments.Count(r => r.MerchantConfirmedAt is not null);

        var byRtoStage = new Dictionary<ShipmentStatus, int>();
        foreach (var returnShipment in returnShipments)
        {
            var scanInfo = await _shipmentTransitionService.GetScanInfoAsync(returnShipment.OriginalShipmentId, cancellationToken);
            var status = scanInfo?.CurrentStatus ?? ShipmentStatus.RtoInitiated;
            byRtoStage[status] = byRtoStage.GetValueOrDefault(status) + 1;
        }

        return new ReturnShipmentSummary(byRtoStage, merchantConfirmed, returnShipments.Count - merchantConfirmed);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Ndr.Tests --filter "FullyQualifiedName~GetReturnShipmentSummaryAsync"`
Expected: PASS

- [ ] **Step 5: Write the failing API test**

Append to `tests/Api.Tests/NdrEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetReturnShipmentsSummary_ReturnsEmptySummaryForNewOrg()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/return-shipments/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("merchantConfirmed").GetInt32());
    }

    [Fact]
    public async Task GetReturnShipmentsSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/return-shipments/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetReturnShipmentsSummary"`
Expected: FAIL — 404.

- [ ] **Step 7: Add the endpoint**

In `NdrEndpoints.cs`, add inside the `returnShipments` group:

```csharp
        returnShipments.MapGet("/summary", async (NdrService service, HttpContext httpContext) =>
            Results.Ok(await service.GetReturnShipmentSummaryAsync(httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("rto.read");
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetReturnShipmentsSummary"`
Expected: PASS (2 tests)

- [ ] **Step 9: Commit backend**

```bash
git add src/Modules/Ndr/Application/NdrService.Summary.cs src/Modules/Ndr/Presentation/NdrEndpoints.cs tests/Ndr.Tests tests/Api.Tests
git commit -m "feat(ndr): add branch-scoped return shipment summary endpoint"
```

### Frontend

- [ ] **Step 10-17: BFF route, hook, and their tests**

```ts
// src/web/src/app/api/return-shipments/summary/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/return-shipments/summary");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

```ts
// src/web/src/features/ndr/hooks/useReturnShipmentsSummary.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface ReturnShipmentSummary {
  byRtoStage: Record<string, number>;
  merchantConfirmed: number;
  pendingConfirmation: number;
}

export function useReturnShipmentsSummary() {
  return useQuery({
    queryKey: ["return-shipments-summary"],
    queryFn: () => clientFetch<ReturnShipmentSummary>("/api/return-shipments/summary"),
  });
}
```

Write `route.test.ts` / `useReturnShipmentsSummary.test.tsx` mirroring Task 2 Steps 10/14 (fixture: `{ byRtoStage: { RtoInitiated: 1, RtoInTransit: 2, RtoAtOrigin: 1, RtoDelivered: 3 }, merchantConfirmed: 3, pendingConfirmation: 4 }`).

- [ ] **Step 18: Wire the page**

Replace the full contents of `src/web/src/app/(staff)/return-shipments/page.tsx`:

```tsx
"use client";

import { Undo2, Truck, PackageCheck, CheckCircle2, Clock } from "lucide-react";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useReturnShipments } from "@/features/ndr/hooks/useReturnShipments";
import { useReturnShipmentsSummary, type ReturnShipmentSummary } from "@/features/ndr/hooks/useReturnShipmentsSummary";
import { useReturnShipmentPanelStore } from "@/features/ndr/store/returnShipmentPanelStore";
import type { ReturnShipment } from "@/features/ndr/types";

function buildTiles(summary: ReturnShipmentSummary): StatTile[] {
  const stage = (key: string) => (summary.byRtoStage[key] ?? 0).toLocaleString();
  return [
    { label: "RTO Initiated", value: stage("RtoInitiated"), icon: Undo2, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "RTO In Transit", value: stage("RtoInTransit"), icon: Truck, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "RTO At Origin", value: stage("RtoAtOrigin"), icon: PackageCheck, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "RTO Delivered", value: stage("RtoDelivered"), icon: CheckCircle2, badgeClass: "bg-status-rto/15 text-status-rto" },
    { label: "Merchant Confirmed", value: summary.merchantConfirmed.toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "Pending Confirmation", value: summary.pendingConfirmation.toLocaleString(), icon: Clock, badgeClass: "bg-muted text-muted-foreground" },
  ];
}

export default function ReturnShipmentsPage() {
  const { data, isLoading, isError, refetch } = useReturnShipments();
  const summary = useReturnShipmentsSummary();
  const { data: branches } = useBranches();
  const openDetail = useReturnShipmentPanelStore((s) => s.openDetail);

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";

  const columns: ColumnDef<ReturnShipment>[] = [
    {
      key: "returnAwb",
      header: "Return AWB",
      render: (r) => (
        <button type="button" onClick={() => openDetail(r.id)} className="font-medium text-foreground hover:underline">
          {r.returnAwb}
        </button>
      ),
    },
    { key: "originalAwb", header: "Original AWB", render: (r) => r.originalAwb },
    { key: "status", header: "Status", render: (r) => <StatusBadge status={r.currentStatus} /> },
    { key: "originBranch", header: "Origin Branch", render: (r) => branchName(r.originBranchId) },
    { key: "destinationBranch", header: "Destination Branch", render: (r) => branchName(r.destinationBranchId) },
    {
      key: "merchantConfirmedAt",
      header: "Merchant Confirmed",
      render: (r) => (r.merchantConfirmedAt ? new Date(r.merchantConfirmedAt).toLocaleDateString() : "—"),
    },
  ];

  const pagedData: PagedResult<ReturnShipment> | undefined = data && {
    items: data,
    page: 1,
    pageSize: data.length,
    totalCount: data.length,
    totalPages: 1,
  };

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Return Shipments</h1>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={6}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load return shipments"
          description="Something went wrong fetching the RTO list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <DataTable
          columns={columns}
          data={pagedData}
          isLoading={false}
          emptyTitle="No return shipments"
          emptyDescription="Shipments resolved as RTO will show up here."
          onPageChange={() => {}}
          rowKey={(r) => r.id}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 19: Page-level test**

Mirror Task 2 Step 19: mock `useReturnShipments`, `useBranches`, `useReturnShipmentsSummary`, `useReturnShipmentPanelStore`; assert `"RTO Initiated"`/`"Merchant Confirmed"` labels render with mocked values.

- [ ] **Step 20: Run all new frontend tests, verify pass**

Run: `npx vitest run src/app/api/return-shipments/summary src/features/ndr/hooks/useReturnShipmentsSummary "src/app/(staff)/return-shipments/page.test.tsx"`

- [ ] **Step 21: Commit frontend**

```bash
git add src/web/src/app/api/return-shipments/summary src/web/src/features/ndr/hooks/useReturnShipmentsSummary.ts src/web/src/features/ndr/hooks/useReturnShipmentsSummary.test.tsx "src/web/src/app/(staff)/return-shipments/page.tsx" "src/web/src/app/(staff)/return-shipments/page.test.tsx"
git commit -m "feat(web): show RTO-stage summary strip on Return Shipments page"
```

---

## Task 8: COD Summary

**Files:**
- Modify: `src/Modules/Cod/Presentation/CodEndpoints.cs`
- Test: `tests/Api.Tests/CodEndpointsTests.cs` (append)
- Create: `src/web/src/app/api/cod/summary/route.ts`
- Test: `src/web/src/app/api/cod/summary/route.test.ts`
- Create: `src/web/src/features/cod/hooks/useCodSummary.ts`
- Test: `src/web/src/features/cod/hooks/useCodSummary.test.tsx`
- Modify: `src/web/src/app/(staff)/cod/page.tsx`

**Interfaces:**
- Consumes: `SummaryStatsBar`/`StatTile` (Task 1); `CodFinancialSummary` type already defined in `src/web/src/features/reports/types.ts` (reused as-is, not redefined).
- Produces: `GET /api/v1/cod/summary` → `CodFinancialSummary { expected, collected, pending, settled, merchantPayable }` (reuses the **existing** `ICodReportingLookupService.GetOrgFinancialSummaryAsync` method verbatim — no Application-layer change in this task at all, only a new endpoint).

### Backend

No new Application-layer method is needed — `GetOrgFinancialSummaryAsync(IReadOnlySet<Guid>? branchIds, ct)` already returns exactly this shape with no date bound. This task is endpoint-only.

- [ ] **Step 1: Write the failing API test**

Append to `tests/Api.Tests/CodEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task GetCodSummary_ReturnsZeroedSummaryForNewOrg()
    {
        var client = _factory.CreateClient();
        var auth = await RegisterOrgAsync(client, "summary");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/cod/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("expected").GetDecimal());
    }

    [Fact]
    public async Task GetCodSummary_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/cod/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetCodSummary"`
Expected: FAIL — 404.

- [ ] **Step 3: Add the endpoint**

In `CodEndpoints.cs`, add inside the `group` (`/api/v1/cod`) block:

```csharp
        group.MapGet("/summary", async (ICodReportingLookupService lookupService, IBranchAuthorizationChecker branchAuthorizationChecker, HttpContext httpContext) =>
        {
            var accessibleBranchIds = await branchAuthorizationChecker.GetAccessibleBranchIdsAsync(httpContext.User, "cod.read", httpContext.RequestAborted);
            var summary = await lookupService.GetOrgFinancialSummaryAsync(accessibleBranchIds, httpContext.RequestAborted);
            return Results.Ok(summary);
        }).RequirePermission("cod.read");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~GetCodSummary"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit backend**

```bash
git add src/Modules/Cod/Presentation/CodEndpoints.cs tests/Api.Tests
git commit -m "feat(cod): expose branch-scoped financial summary endpoint"
```

### Frontend

- [ ] **Step 6: Write the failing BFF route test**

```ts
// src/web/src/app/api/cod/summary/route.test.ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/cod/summary", () => {
  it("proxies to the backend summary endpoint", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ expected: 100, collected: 80, pending: 20, settled: 60, merchantPayable: 20 }),
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/cod/summary");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 7: Run test to verify it fails**

Run: `npx vitest run src/app/api/cod/summary/route.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 8: Write the route**

```ts
// src/web/src/app/api/cod/summary/route.ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/cod/summary");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 9: Run test to verify it passes**

Run: `npx vitest run src/app/api/cod/summary/route.test.ts`
Expected: PASS

- [ ] **Step 10: Write the failing hook test**

```tsx
// src/web/src/features/cod/hooks/useCodSummary.test.tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useCodSummary } from "./useCodSummary";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useCodSummary", () => {
  it("fetches the summary proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ expected: 100, collected: 80, pending: 20, settled: 60, merchantPayable: 20 }),
    });

    const { result } = renderHook(() => useCodSummary(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.pending).toBe(20);
    expect(global.fetch).toHaveBeenCalledWith("/api/cod/summary", undefined);
  });
});
```

- [ ] **Step 11: Run test to verify it fails**

Run: `npx vitest run src/features/cod/hooks/useCodSummary.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 12: Write the hook**

```ts
// src/web/src/features/cod/hooks/useCodSummary.ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CodFinancialSummary } from "@/features/reports/types";

export function useCodSummary() {
  return useQuery({
    queryKey: ["cod-summary"],
    queryFn: () => clientFetch<CodFinancialSummary>("/api/cod/summary"),
  });
}
```

- [ ] **Step 13: Run test to verify it passes**

Run: `npx vitest run src/features/cod/hooks/useCodSummary.test.tsx`
Expected: PASS

- [ ] **Step 14: Wire the page**

Replace the full contents of `src/web/src/app/(staff)/cod/page.tsx` (a pure addition — this page has no early-return loading/error guards to restructure, since each tab already manages its own loading state independently):

```tsx
"use client";

import { useState } from "react";
import { Wallet, CheckCircle2, Clock, Landmark, Building2 } from "lucide-react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { useCodSummary } from "@/features/cod/hooks/useCodSummary";
import type { CodFinancialSummary } from "@/features/reports/types";
import { CollectionsTab } from "@/features/cod/components/CollectionsTab";
import { CollectionDetailSheet } from "@/features/cod/components/CollectionDetailSheet";
import { RiderSettlementsTab } from "@/features/cod/components/RiderSettlementsTab";
import { RiderSettlementCreateSheet } from "@/features/cod/components/RiderSettlementCreateSheet";
import { RiderSettlementDetailSheet } from "@/features/cod/components/RiderSettlementDetailSheet";
import { BranchSettlementsTab } from "@/features/cod/components/BranchSettlementsTab";
import { BranchSettlementCreateSheet } from "@/features/cod/components/BranchSettlementCreateSheet";
import { BranchSettlementDetailSheet } from "@/features/cod/components/BranchSettlementDetailSheet";
import { MerchantSettlementsTab } from "@/features/cod/components/MerchantSettlementsTab";
import { MerchantSettlementCreateSheet } from "@/features/cod/components/MerchantSettlementCreateSheet";
import { MerchantSettlementDetailSheet } from "@/features/cod/components/MerchantSettlementDetailSheet";
import { AdjustmentsTab } from "@/features/cod/components/AdjustmentsTab";
import { AdjustmentDetailSheet } from "@/features/cod/components/AdjustmentDetailSheet";
import { ReconciliationTab } from "@/features/cod/components/ReconciliationTab";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

function buildTiles(summary: CodFinancialSummary): StatTile[] {
  return [
    { label: "Expected", value: currencyFormatter.format(summary.expected), icon: Wallet, badgeClass: "bg-muted text-muted-foreground" },
    { label: "Collected", value: currencyFormatter.format(summary.collected), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered" },
    { label: "Pending", value: currencyFormatter.format(summary.pending), icon: Clock, badgeClass: "bg-accent text-primary", highlight: true },
    { label: "Settled", value: currencyFormatter.format(summary.settled), icon: Landmark, badgeClass: "bg-status-received/15 text-status-received" },
    { label: "Merchant Payable", value: currencyFormatter.format(summary.merchantPayable), icon: Building2, badgeClass: "bg-status-sealed/15 text-status-sealed" },
  ];
}

const TABS = [
  { key: "collections", label: "Collections" },
  { key: "rider-settlements", label: "Rider Settlements" },
  { key: "branch-settlements", label: "Branch Settlements" },
  { key: "merchant-settlements", label: "Merchant Settlements" },
  { key: "adjustments", label: "Adjustments" },
  { key: "reconciliation", label: "Reconciliation" },
] as const;

type TabKey = (typeof TABS)[number]["key"];

export default function CodPage() {
  const [activeTab, setActiveTab] = useState<TabKey>("collections");
  const summary = useCodSummary();

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">COD</h1>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={5}
      />
      <Tabs value={activeTab} onValueChange={(v) => setActiveTab(v as TabKey)}>
        <TabsList>
          {TABS.map((tab) => (
            <TabsTrigger key={tab.key} value={tab.key}>{tab.label}</TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="collections"><CollectionsTab /></TabsContent>
        <TabsContent value="rider-settlements"><RiderSettlementsTab /></TabsContent>
        <TabsContent value="branch-settlements"><BranchSettlementsTab /></TabsContent>
        <TabsContent value="merchant-settlements"><MerchantSettlementsTab /></TabsContent>
        <TabsContent value="adjustments"><AdjustmentsTab /></TabsContent>
        <TabsContent value="reconciliation"><ReconciliationTab /></TabsContent>
      </Tabs>

      <CollectionDetailSheet />
      <RiderSettlementCreateSheet />
      <RiderSettlementDetailSheet />
      <BranchSettlementCreateSheet />
      <BranchSettlementDetailSheet />
      <MerchantSettlementCreateSheet />
      <MerchantSettlementDetailSheet />
      <AdjustmentDetailSheet />
    </div>
  );
}
```

- [ ] **Step 15: Page-level test**

Create `src/web/src/app/(staff)/cod/page.test.tsx` (no page-level test exists yet for this page), mocking `useCodSummary` and stubbing every tab/sheet component this page imports so the test only exercises the page's own summary-strip wiring, not any tab's internals:

```tsx
// src/web/src/app/(staff)/cod/page.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import CodPage from "./page";

vi.mock("@/features/cod/hooks/useCodSummary", () => ({
  useCodSummary: () => ({ data: { expected: 5000, collected: 3000, pending: 2000, settled: 1000, merchantPayable: 900 }, isLoading: false, isError: false }),
}));
vi.mock("@/features/cod/components/CollectionsTab", () => ({ CollectionsTab: () => null }));
vi.mock("@/features/cod/components/CollectionDetailSheet", () => ({ CollectionDetailSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementsTab", () => ({ RiderSettlementsTab: () => null }));
vi.mock("@/features/cod/components/RiderSettlementCreateSheet", () => ({ RiderSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/RiderSettlementDetailSheet", () => ({ RiderSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementsTab", () => ({ BranchSettlementsTab: () => null }));
vi.mock("@/features/cod/components/BranchSettlementCreateSheet", () => ({ BranchSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/BranchSettlementDetailSheet", () => ({ BranchSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementsTab", () => ({ MerchantSettlementsTab: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementCreateSheet", () => ({ MerchantSettlementCreateSheet: () => null }));
vi.mock("@/features/cod/components/MerchantSettlementDetailSheet", () => ({ MerchantSettlementDetailSheet: () => null }));
vi.mock("@/features/cod/components/AdjustmentsTab", () => ({ AdjustmentsTab: () => null }));
vi.mock("@/features/cod/components/AdjustmentDetailSheet", () => ({ AdjustmentDetailSheet: () => null }));
vi.mock("@/features/cod/components/ReconciliationTab", () => ({ ReconciliationTab: () => null }));

describe("CodPage", () => {
  it("renders the financial summary strip above the tabs", () => {
    render(<CodPage />);
    expect(screen.getByText("Expected")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument();
  });
});
```

- [ ] **Step 16: Run test to verify it passes**

Run: `npx vitest run "src/app/(staff)/cod/page.test.tsx"`
Expected: PASS

- [ ] **Step 17: Commit frontend**

```bash
git add src/web/src/app/api/cod/summary src/web/src/features/cod/hooks/useCodSummary.ts src/web/src/features/cod/hooks/useCodSummary.test.tsx "src/web/src/app/(staff)/cod/page.tsx" "src/web/src/app/(staff)/cod/page.test.tsx"
git commit -m "feat(web): show financial summary strip on COD page"
```

---

## Final Verification (after all 8 tasks)

- [ ] Run the full backend suite: `dotnet test` from the repo root — confirm no regressions in any module, especially `Shipments.Tests`, `Branches.Tests`, `Riders.Tests`, `Manifests.Tests`, `Ndr.Tests`, `Cod.Tests`, `Api.Tests`.
- [ ] Run the full frontend suite: `npm test` from `src/web` — confirm no regressions across every existing page/hook test.
- [ ] Run `npm run build` from `src/web` — confirm the Next.js production build succeeds (catches any type error across the 7 modified pages/hooks/types).
- [ ] Manually smoke-test at least one page (e.g. Shipments) against the running dev stack: confirm the strip renders real counts, and that toggling a restrictive role's branch grant changes the numbers shown.
