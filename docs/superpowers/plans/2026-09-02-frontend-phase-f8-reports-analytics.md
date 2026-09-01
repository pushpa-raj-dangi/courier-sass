# Frontend Phase F8 — Reports & Analytics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the real gaps between Phase 13's reporting/export backend and the `/dashboard` page Frontend Phase F1 built: surface the already-returned `RiderPerformance` data, add a date-range control to the organization dashboard, and build UI for both CSV export endpoints (which currently have none).

**Architecture:** 1 backend task adds `Content-Disposition: attachment` to both CSV export routes (a real bug — without it, browsers try to render the CSV instead of downloading it). 5 frontend tasks extend the existing `src/web/src/features/reports/` folder and `/dashboard` page — no new nav entry, no new route, matching every prior phase's practice of following established conventions rather than inventing new ones.

**Tech Stack:** ASP.NET Core / EF Core / xUnit (backend); Next.js 14 App Router, TanStack Query, React Hook Form-free (plain controlled inputs, matching `ReconciliationTab`'s precedent), Recharts (`@/components/ui/chart`), Vitest + Testing Library (frontend). No new npm/NuGet packages.

**Spec:** `docs/superpowers/specs/2026-09-01-frontend-phase-f8-reports-analytics-design.md`

## Global Constraints

- No new nav entry and no new route — every addition lands in the existing `src/web/src/features/reports/` folder and the existing `/dashboard` page, per the spec's framing of F8 as filling out F1's dashboard, not building a separate surface.
- Every date value that leaves the browser for a `DateTimeOffset` backend field must be anchored to explicit UTC (`${value}T00:00:00Z` / `${value}T23:59:59Z`) before it reaches a query string — this project's API host runs in Nepal Standard Time (UTC+5:45), and a bare `"YYYY-MM-DD"` string lets .NET's `DateTimeOffset` JSON/model-binding default to the host's local offset, which Npgsql's `timestamptz` writer rejects outright (the exact bug Frontend Phase F7 hit and fixed for `MerchantSettlementCreateSheet`). Task 4 and Task 5 both carry this requirement.
- Riders have no display-name field on `RiderResponse` — every rider-identifying UI in this app (COD's Rider Settlements tab, `AssignTaskSheet`) labels riders by `licenseNumber`, and Task 3 does the same. Don't invent a name field.
- Backend test command: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ReportsEndpointsTests` (integration, needs the local Postgres container per `[[Courier Local Postgres]]` — already running on port 5434, do not start a second one). Frontend: `cd src/web && npm test` (Vitest) and `npm run build` (production build/type-check) after every frontend task.

---

## Task 1: `Content-Disposition: attachment` on both CSV export routes

**Files:**
- Modify: `src/Modules/Reports/Presentation/ReportsEndpoints.cs`
- Modify: `tests/Api.Tests/ReportsEndpointsTests.cs`

**Interfaces:**
- Produces: `HandleCsvAsync` gains a `fileNamePrefix` parameter; both `GET /api/v1/reports/export/shipments` and `GET /api/v1/reports/export/cod-summary` now respond with a `Content-Disposition: attachment; filename="<prefix>-<UTC-timestamp>.csv"` header, in addition to the existing `Content-Type: text/csv`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Api.Tests/ReportsEndpointsTests.cs`, immediately after `ExportShipments_ReturnsCsvWithHeaderRow`:

```csharp
    [Fact]
    public async Task ExportShipments_SetsContentDispositionAttachmentWithCsvFilename()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-ship-disp");

        var response = await client.GetAsync("/api/v1/reports/export/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.StartsWith("shipments-", disposition.FileName?.Trim('"'));
        Assert.EndsWith(".csv", disposition.FileName?.Trim('"'));
    }
```

Add immediately after `ExportCodSummary_ReturnsCsvWithHeaderRow`:

```csharp
    [Fact]
    public async Task ExportCodSummary_SetsContentDispositionAttachmentWithCsvFilename()
    {
        var (client, _) = await RegisterOrgWithBranchAsync("export-cod-disp");

        var response = await client.GetAsync("/api/v1/reports/export/cod-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition!.DispositionType);
        Assert.StartsWith("cod-summary-", disposition.FileName?.Trim('"'));
        Assert.EndsWith(".csv", disposition.FileName?.Trim('"'));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter "ExportShipments_SetsContentDispositionAttachmentWithCsvFilename|ExportCodSummary_SetsContentDispositionAttachmentWithCsvFilename"`
Expected: FAIL — `disposition` is null (no `Content-Disposition` header exists yet).

- [ ] **Step 3: Add the header**

In `src/Modules/Reports/Presentation/ReportsEndpoints.cs`, update the two `MapGet` call sites and `HandleCsvAsync`'s signature:

```csharp
        group.MapGet("/export/shipments", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(httpContext, "shipments", () => service.GetShipmentsCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");

        group.MapGet("/export/cod-summary", async (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, ReportExportService service, HttpContext httpContext) =>
            await HandleCsvAsync(httpContext, "cod-summary", () => service.GetCodSummaryCsvAsync(branchId, from, to, httpContext.User, httpContext.RequestAborted)))
            .RequirePermission("report.export");
```

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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter ReportsEndpointsTests`
Expected: PASS — the full `ReportsEndpointsTests` file, including every pre-existing test plus the 2 new ones.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Reports/Presentation/ReportsEndpoints.cs tests/Api.Tests/ReportsEndpointsTests.cs
git commit -m "fix(reports): set Content-Disposition: attachment on both CSV export routes"
```

---

## Task 2: `riderPerformance` type + date-range query forwarding on `useOrgDashboard`

**Files:**
- Modify: `src/web/src/features/reports/types.ts`
- Modify: `src/web/src/app/api/reports/dashboard/organization/route.ts`
- Modify: `src/web/src/features/reports/hooks/useOrgDashboard.ts`
- Modify: `src/web/src/features/reports/hooks/useOrgDashboard.test.tsx`

**Interfaces:**
- Consumes: `clientFetch` (`@/lib/api/clientFetch`), `apiFetch` (`@/lib/auth/apiFetch`).
- Produces: `RiderPerformance` type; `OrgDashboardSummary.riderPerformance: RiderPerformance[]`. `useOrgDashboard(params?: { from?: string; to?: string })` — backward-compatible with the existing no-args call (empty query string produces the exact same URL the current test already asserts).

- [ ] **Step 1: Write the failing test**

Add to `src/web/src/features/reports/hooks/useOrgDashboard.test.tsx`, after the existing `it(...)` block (inside the same `describe`):

```typescript
  it("forwards from/to as a query string when provided", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        todaysShipmentsByStatus: {},
        financial: { expected: 0, collected: 0, pending: 0, settled: 0, merchantPayable: 0 },
        branchPerformance: {},
        riderPerformance: [],
        overallDeliverySuccessRate: null,
        overallNdrRate: null,
        overallRtoRate: null,
        averageDeliveryTime: null,
      }),
    });

    const { result } = renderHook(
      () => useOrgDashboard({ from: "2026-08-01T00:00:00Z", to: "2026-08-31T23:59:59Z" }),
      { wrapper }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(global.fetch).toHaveBeenCalledWith(
      "/api/reports/dashboard/organization?from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z",
      undefined
    );
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- useOrgDashboard.test.tsx`
Expected: FAIL — `useOrgDashboard` doesn't accept a `params` argument yet (compile error) and always calls the bare URL.

- [ ] **Step 3: Add the type**

In `src/web/src/features/reports/types.ts`, add after the existing `BranchPerformance` interface and before `OrgDashboardSummary`:

```typescript
export interface RiderPerformance {
  riderId: string;
  attempts: number;
  successes: number;
  successRate: number | null;
}
```

Then add `riderPerformance: RiderPerformance[];` as a new field on `OrgDashboardSummary`, immediately after `branchPerformance`:

```typescript
export interface OrgDashboardSummary {
  todaysShipmentsByStatus: Record<string, number>;
  financial: CodFinancialSummary;
  branchPerformance: Record<string, BranchPerformance>;
  riderPerformance: RiderPerformance[];
  overallDeliverySuccessRate: number | null;
  overallNdrRate: number | null;
  overallRtoRate: number | null;
  // .NET TimeSpan's default JSON format, e.g. "02:15:30" (hh:mm:ss) or "1.02:15:30" (d.hh:mm:ss)
  averageDeliveryTime: string | null;
}
```

- [ ] **Step 4: Forward the query string in the BFF route**

Replace `src/web/src/app/api/reports/dashboard/organization/route.ts` in full:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/reports/dashboard/organization${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 5: Update the hook**

Replace `src/web/src/features/reports/hooks/useOrgDashboard.ts` in full:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrgDashboardSummary } from "@/features/reports/types";

export function useOrgDashboard(params: { from?: string; to?: string } = {}) {
  const { from, to } = params;
  const query = new URLSearchParams();
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const search = query.toString();

  return useQuery({
    queryKey: ["org-dashboard", from ?? null, to ?? null],
    queryFn: () => clientFetch<OrgDashboardSummary>(`/api/reports/dashboard/organization${search ? `?${search}` : ""}`),
  });
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `cd src/web && npm test -- useOrgDashboard.test.tsx`
Expected: PASS (2 tests — the pre-existing no-args test still passes unchanged, proving the empty-query-string path produces the identical URL it always did).

- [ ] **Step 7: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS — no existing tests broken, no type errors (note: `DashboardPage` doesn't read `riderPerformance` yet — that's Task 3 — so this step only proves the type/hook/BFF plumbing compiles and works in isolation).

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/reports/types.ts src/web/src/app/api/reports/dashboard/organization/route.ts src/web/src/features/reports/hooks/useOrgDashboard.ts src/web/src/features/reports/hooks/useOrgDashboard.test.tsx
git commit -m "feat(reports-frontend): add riderPerformance type and date-range forwarding to useOrgDashboard"
```

---

## Task 3: `RiderPerformanceChart` component, wired into the dashboard

**Files:**
- Create: `src/web/src/features/reports/components/RiderPerformanceChart.tsx`
- Create: `src/web/src/features/reports/components/RiderPerformanceChart.test.tsx`
- Modify: `src/web/src/app/(staff)/dashboard/page.tsx`

**Interfaces:**
- Consumes: `RiderPerformance` (Task 2's `types.ts`), `useRiders` (`@/features/riders/hooks/useRiders`), `EmptyState` (`@/components/states/EmptyState`), `ChartContainer`/`ChartTooltip`/`ChartTooltipContent`/`ChartConfig` (`@/components/ui/chart`).
- Produces: `RiderPerformanceChart({ data: RiderPerformance[] })` component, structurally identical to `BranchPerformanceChart` (bar chart + sorted detail-rows-below + `EmptyState`), labeling riders by `licenseNumber`.

- [ ] **Step 1: Write the failing tests**

`src/web/src/features/reports/components/RiderPerformanceChart.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RiderPerformanceChart } from "./RiderPerformanceChart";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders(data: Parameters<typeof RiderPerformanceChart>[0]["data"]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RiderPerformanceChart data={data} />
    </QueryClientProvider>
  );
}

describe("RiderPerformanceChart", () => {
  it("sorts riders by delivery attempts, descending, and shows their license number and success rate", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "LIC-001", roleType: "DeliveryRider", isActive: true },
      { id: "r2", userId: "u2", baseBranchId: "b1", licenseNumber: "LIC-002", roleType: "DeliveryRider", isActive: true },
    ]);

    const { container } = renderWithProviders([
      { riderId: "r1", attempts: 5, successes: 4, successRate: 0.8 },
      { riderId: "r2", attempts: 20, successes: 18, successRate: 0.9 },
    ]);

    // Same scoping rationale as BranchPerformanceChart.test.tsx: the chart's own SVG axis labels
    // are covered elsewhere — here, scope to the plain-text rate-stats row below the chart.
    await waitFor(() => {
      const labels = Array.from(container.querySelectorAll(".border-t span.truncate")).map(
        (el) => el.textContent
      );
      expect(labels).toEqual(["LIC-002", "LIC-001"]);
    });
  });

  it("shows an empty state when there is no rider performance data", () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([]);
    renderWithProviders([]);
    expect(screen.getByText("No rider activity yet")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/web && npm test -- RiderPerformanceChart.test.tsx`
Expected: FAIL — `./RiderPerformanceChart` doesn't exist yet.

- [ ] **Step 3: Build the component**

`src/web/src/features/reports/components/RiderPerformanceChart.tsx`:

```tsx
"use client";

import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts";
import { Bike } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/states/EmptyState";
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart";
import { useRiders } from "@/features/riders/hooks/useRiders";
import type { RiderPerformance } from "@/features/reports/types";

function formatPercent(value: number | null): string {
  return value === null ? "—" : `${(value * 100).toFixed(1)}%`;
}

const chartConfig = {
  attempts: { label: "Delivery Attempts", color: "hsl(var(--primary))" },
} satisfies ChartConfig;

export function RiderPerformanceChart({ data }: { data: RiderPerformance[] }) {
  const { data: riders } = useRiders();
  const riderLabel = (id: string) => riders?.find((r) => r.id === id)?.licenseNumber ?? "Unknown rider";

  const rows = data
    .map((perf) => ({ riderId: perf.riderId, label: riderLabel(perf.riderId), perf }))
    .sort((a, b) => b.perf.attempts - a.perf.attempts);

  const chartData = rows.map((row) => ({ rider: row.label, attempts: row.perf.attempts }));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Bike className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Rider Performance
        </CardTitle>
        <CardDescription>Delivery attempts by rider</CardDescription>
      </CardHeader>
      <CardContent>
        {rows.length === 0 ? (
          <EmptyState title="No rider activity yet" description="Rider performance appears once delivery attempts start recording." icon={Bike} />
        ) : (
          <div className="space-y-6">
            <ChartContainer config={chartConfig} className="aspect-auto h-[300px] w-full">
              <BarChart accessibilityLayer data={chartData} margin={{ top: 8, bottom: 24 }}>
                <CartesianGrid vertical={false} />
                <XAxis
                  dataKey="rider"
                  tickLine={false}
                  axisLine={false}
                  interval={0}
                  angle={-35}
                  textAnchor="end"
                  height={56}
                  tickMargin={8}
                />
                <YAxis dataKey="attempts" type="number" hide />
                <ChartTooltip cursor={{ fill: "hsl(var(--muted))" }} content={<ChartTooltipContent indicator="line" />} />
                <Bar dataKey="attempts" fill="var(--color-attempts)" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ChartContainer>
            <div className="space-y-2 border-t border-border pt-4">
              {rows.map((row) => (
                <div key={row.riderId} className="flex items-center justify-between text-xs text-muted-foreground">
                  <span className="truncate">{row.label}</span>
                  <span className="tabular-nums">
                    {row.perf.successes}/{row.perf.attempts} delivered &middot; {formatPercent(row.perf.successRate)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/web && npm test -- RiderPerformanceChart.test.tsx`
Expected: PASS (2 tests)

- [ ] **Step 5: Wire it into the dashboard**

In `src/web/src/app/(staff)/dashboard/page.tsx`, add the import:

```tsx
import { RiderPerformanceChart } from "@/features/reports/components/RiderPerformanceChart";
```

Change the first grid row from a 2-column to a 3-column layout and add the new chart:

```tsx
      <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <ShipmentFunnel data={data.todaysShipmentsByStatus} />
        <BranchPerformanceChart data={data.branchPerformance} />
        <RiderPerformanceChart data={data.riderPerformance} />
      </div>
```

- [ ] **Step 6: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/reports/components/RiderPerformanceChart.tsx src/web/src/features/reports/components/RiderPerformanceChart.test.tsx "src/web/src/app/(staff)/dashboard/page.tsx"
git commit -m "feat(reports-frontend): add Rider Performance chart to the organization dashboard"
```

---

## Task 4: Date-range control on the organization dashboard

**Files:**
- Modify: `src/web/src/features/reports/components/RateTiles.tsx`
- Create: `src/web/src/features/reports/components/RateTiles.test.tsx`
- Modify: `src/web/src/app/(staff)/dashboard/page.tsx`

**Interfaces:**
- Consumes: `useOrgDashboard` (Task 2), `Input`/`Label` (`@/components/ui/input`, `@/components/ui/label`).
- Produces: `RateTiles` gains an optional `rangeLabel?: string` prop (defaults to `"Last 30 days"`, preserving today's behavior for any caller that doesn't pass it). `DashboardPage` gains a persistent From/To date filter row (visible during loading/error too, so a bad or slow query doesn't hide the controls needed to fix it) whose values are UTC-anchored before reaching `useOrgDashboard`.

- [ ] **Step 1: Write the failing test**

`src/web/src/features/reports/components/RateTiles.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RateTiles } from "./RateTiles";

const data = { overallNdrRate: 0.05, overallRtoRate: 0.02, averageDeliveryTime: "02:15:30" };

describe("RateTiles", () => {
  it("defaults the range caption to Last 30 days when none is provided", () => {
    render(<RateTiles data={data} />);
    expect(screen.getByText("Last 30 days")).toBeInTheDocument();
  });

  it("shows a custom range caption when provided", () => {
    render(<RateTiles data={data} rangeLabel="2026-08-01 – 2026-08-31" />);
    expect(screen.getByText("2026-08-01 – 2026-08-31")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- RateTiles.test.tsx`
Expected: FAIL — `rangeLabel` prop doesn't exist yet; the second test can't find the custom caption text (it renders the hardcoded `"Last 30 days"` instead).

- [ ] **Step 3: Add the prop**

In `src/web/src/features/reports/components/RateTiles.tsx`, change the component signature and the hardcoded caption:

```tsx
export function RateTiles({
  data,
  rangeLabel = "Last 30 days",
}: {
  data: {
    overallNdrRate: number | null;
    overallRtoRate: number | null;
    averageDeliveryTime: string | null;
  };
  rangeLabel?: string;
}) {
```

```tsx
        <CardDescription>{rangeLabel}</CardDescription>
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- RateTiles.test.tsx`
Expected: PASS (2 tests)

- [ ] **Step 5: Add the date filter row to the dashboard**

Replace `src/web/src/app/(staff)/dashboard/page.tsx` in full:

```tsx
"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useOrgDashboard } from "@/features/reports/hooks/useOrgDashboard";
import { ShipmentFunnel } from "@/features/reports/components/ShipmentFunnel";
import { FinancialSummary } from "@/features/reports/components/FinancialSummary";
import { RateTiles } from "@/features/reports/components/RateTiles";
import { DeliveryRateHero } from "@/features/reports/components/DeliveryRateHero";
import { BranchPerformanceChart } from "@/features/reports/components/BranchPerformanceChart";
import { RiderPerformanceChart } from "@/features/reports/components/RiderPerformanceChart";

export default function DashboardPage() {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  // UTC-anchored before leaving the browser — a bare "YYYY-MM-DD" lets .NET's DateTimeOffset
  // deserializer default to the API host's local offset (Nepal, UTC+5:45), which Npgsql's
  // timestamptz reads/writes reject outright (the exact bug Frontend Phase F7 hit and fixed).
  const { data, isLoading, isError, refetch } = useOrgDashboard({
    from: from ? `${from}T00:00:00Z` : undefined,
    to: to ? `${to}T23:59:59Z` : undefined,
  });
  const rangeLabel = from && to ? `${from} – ${to}` : "Last 30 days";

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="dashboard-from">From</Label>
          <Input id="dashboard-from" aria-label="From date" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </div>
        <div className="space-y-1">
          <Label htmlFor="dashboard-to">To</Label>
          <Input id="dashboard-to" aria-label="To date" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
      </div>

      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && (isError || !data) && (
        <ErrorState
          title="Couldn't load the dashboard"
          description="Something went wrong fetching today's summary."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && data && (
        <>
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
            <ShipmentFunnel data={data.todaysShipmentsByStatus} />
            <BranchPerformanceChart data={data.branchPerformance} />
            <RiderPerformanceChart data={data.riderPerformance} />
          </div>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
            <DeliveryRateHero rate={data.overallDeliverySuccessRate} />
            <RateTiles
              data={{
                overallNdrRate: data.overallNdrRate,
                overallRtoRate: data.overallRtoRate,
                averageDeliveryTime: data.averageDeliveryTime,
              }}
              rangeLabel={rangeLabel}
            />
          </div>
          <FinancialSummary data={data.financial} />
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 6: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/reports/components/RateTiles.tsx src/web/src/features/reports/components/RateTiles.test.tsx "src/web/src/app/(staff)/dashboard/page.tsx"
git commit -m "feat(reports-frontend): add date-range filter to the organization dashboard"
```

---

## Task 5: `ExportReportsCard` + CSV export BFF passthrough routes

**Files:**
- Create: `src/web/src/app/api/reports/export/shipments/route.ts`
- Create: `src/web/src/app/api/reports/export/cod-summary/route.ts`
- Create: `src/web/src/features/reports/components/ExportReportsCard.tsx`
- Create: `src/web/src/features/reports/components/ExportReportsCard.test.tsx`
- Modify: `src/web/src/app/(staff)/dashboard/page.tsx`

**Interfaces:**
- Consumes: `apiFetch` (`@/lib/auth/apiFetch`), `useBranches` (`@/features/branches/hooks/useBranches`), `usePermission` (`@/features/auth/hooks/usePermission`), shadcn `Select`/`Button` (`asChild`).
- Produces: two BFF routes that stream the CSV response body and headers through unchanged (not `NextResponse.json`, since these must stay downloadable files). `ExportReportsCard` component: branch + date-range filters, two download links, gated on `report.export`.

- [ ] **Step 1: Write the failing tests**

`src/web/src/features/reports/components/ExportReportsCard.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }),
}));

const mockUsePermission = vi.fn();
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: () => mockUsePermission(),
}));

import { ExportReportsCard } from "./ExportReportsCard";

describe("ExportReportsCard", () => {
  it("renders nothing when the caller lacks report.export", () => {
    mockUsePermission.mockReturnValue(false);
    const { container } = render(<ExportReportsCard />);
    expect(container).toBeEmptyDOMElement();
  });

  it("builds export links with the selected branch and UTC-anchored dates", () => {
    mockUsePermission.mockReturnValue(true);
    render(<ExportReportsCard />);

    fireEvent.click(screen.getByLabelText("Filter by branch"));
    fireEvent.click(screen.getByRole("option", { name: "Kathmandu Hub" }));
    fireEvent.change(screen.getByLabelText("Export from date"), { target: { value: "2026-08-01" } });
    fireEvent.change(screen.getByLabelText("Export to date"), { target: { value: "2026-08-31" } });

    const shipmentsLink = screen.getByRole("link", { name: /export shipments/i });
    expect(shipmentsLink).toHaveAttribute(
      "href",
      "/api/reports/export/shipments?branchId=b1&from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z"
    );
    const codLink = screen.getByRole("link", { name: /export cod summary/i });
    expect(codLink).toHaveAttribute(
      "href",
      "/api/reports/export/cod-summary?branchId=b1&from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z"
    );
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/web && npm test -- ExportReportsCard.test.tsx`
Expected: FAIL — `./ExportReportsCard` doesn't exist yet.

- [ ] **Step 3: Add the two BFF passthrough routes**

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

`src/web/src/app/api/reports/export/cod-summary/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/reports/export/cod-summary${query}`);
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

- [ ] **Step 4: Build the component**

`src/web/src/features/reports/components/ExportReportsCard.tsx`:

```tsx
"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { usePermission } from "@/features/auth/hooks/usePermission";

export function ExportReportsCard() {
  const [branchId, setBranchId] = useState<string>("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const { data: branches } = useBranches();
  const canExport = usePermission("report.export");

  if (!canExport) {
    return null;
  }

  // Same UTC-anchoring requirement as the dashboard's own date filter (Task 4) — these query
  // strings feed the identical backend DateTimeOffset parameters.
  function buildQuery(): string {
    const query = new URLSearchParams();
    if (branchId !== "all") query.set("branchId", branchId);
    if (from) query.set("from", `${from}T00:00:00Z`);
    if (to) query.set("to", `${to}T23:59:59Z`);
    const search = query.toString();
    return search ? `?${search}` : "";
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Download className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Export Reports
        </CardTitle>
        <CardDescription>Download CSV reports for a branch and date range</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <Label htmlFor="export-branch">Branch</Label>
            <Select value={branchId} onValueChange={setBranchId}>
              <SelectTrigger id="export-branch" className="w-48" aria-label="Filter by branch">
                <SelectValue placeholder="All branches" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All branches</SelectItem>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1">
            <Label htmlFor="export-from">From</Label>
            <Input id="export-from" aria-label="Export from date" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="export-to">To</Label>
            <Input id="export-to" aria-label="Export to date" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </div>
        </div>
        <div className="flex flex-wrap gap-3">
          <Button asChild variant="outline">
            <a href={`/api/reports/export/shipments${buildQuery()}`}>Export Shipments (CSV)</a>
          </Button>
          <Button asChild variant="outline">
            <a href={`/api/reports/export/cod-summary${buildQuery()}`}>Export COD Summary (CSV)</a>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/web && npm test -- ExportReportsCard.test.tsx`
Expected: PASS (2 tests)

- [ ] **Step 6: Wire it into the dashboard**

In `src/web/src/app/(staff)/dashboard/page.tsx`, add the import:

```tsx
import { ExportReportsCard } from "@/features/reports/components/ExportReportsCard";
```

Add it after `<FinancialSummary data={data.financial} />`:

```tsx
          <FinancialSummary data={data.financial} />
          <ExportReportsCard />
```

- [ ] **Step 7: Run the full frontend test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/web/src/app/api/reports/export src/web/src/features/reports/components/ExportReportsCard.tsx src/web/src/features/reports/components/ExportReportsCard.test.tsx "src/web/src/app/(staff)/dashboard/page.tsx"
git commit -m "feat(reports-frontend): add Export Reports card with CSV download links"
```

---

## Task 6: Manual E2E verification and `FRONTEND-ROADMAP.md` completion

**Files:**
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: nothing new — this task exercises Tasks 1–5's full stack end-to-end against the real API and Postgres, per this project's established practice that every prior phase's own manual pass has found real bugs invisible to unit tests alone.

- [ ] **Step 1: Start the backend and frontend**

Ensure `courier-postgres-1`, `courier-redis-1`, `courier-rabbitmq-1` are running. Run the API (`dotnet run --project src/Api`) and the frontend (`cd src/web && npm run dev`).

- [ ] **Step 2: Seed data with rider activity via the API**

Register a fresh org, seed a branch pair with zones/rate-card, a merchant, a pickup rider and a delivery rider (the same shapes `ReportsEndpointsTests.cs`/`CodEndpointsTests.cs` already use), then book and fully deliver at least one COD shipment through the real pickup → manifest → delivery flow (F3–F5's existing flows) so the delivery rider has a real completed attempt to show up in `riderPerformance`.

- [ ] **Step 3: Verify the dashboard**

Log in to the staff web UI as the registered owner and open `/dashboard`. Confirm the new **Rider Performance** chart shows the delivery rider (labeled by license number) with 1 attempt / 1 success / 100% rate. Set the From/To date filter to a range excluding today and confirm the dashboard's figures (shipment funnel, rider performance, rates) drop to reflect an empty window; reset to a range including today and confirm the real numbers return. Confirm the **Key Rates** card's caption switches from "Last 30 days" to the literal date range while a custom filter is active.

- [ ] **Step 4: Verify the exports**

In the new **Export Reports** card, click "Export Shipments (CSV)" with no filters — confirm the browser downloads (not opens inline) a file whose name starts with `shipments-` and ends `.csv`, and that it contains a header row plus the booked shipment's row. Repeat for "Export COD Summary (CSV)". Then filter to the seeded branch and confirm both exports still return exactly the same rows (single-branch scenario); if a second branch is easy to seed in the same session, book a shipment there too and confirm the branch-filtered export excludes it.

- [ ] **Step 5: Verify the permission gate**

Confirm the **Export Reports** card doesn't render for a caller without `report.export` (any role lacking that permission, or check via the network tab that the underlying backend call would 403 without it — CLAUDE.md §33's server-side enforcement is the real gate, this step only confirms the UI polish matches).

- [ ] **Step 6: Fix any real bugs found**

If Steps 3–5 surface a real defect, fix it now with a small targeted commit before proceeding — do not defer a bug found during this phase's own E2E pass, matching every prior phase's practice.

- [ ] **Step 7: Update `docs/FRONTEND-ROADMAP.md`**

Replace the Phase F8 section's `*Status: Not started.*` line:

```markdown
*Status: Complete.* Closed the 4 real gaps found while investigating: `OrgDashboardResponse.RiderPerformance`
was already returned by the backend but silently dropped by the frontend (new `RiderPerformanceChart`,
structurally identical to `BranchPerformanceChart`, labeling riders by `licenseNumber` per every other
rider-identifying UI in this app); the organization dashboard had no date-range control despite the
backend already supporting one (new From/To filter, UTC-anchored per Frontend Phase F7's own lesson,
also fixing `RateTiles`' previously-hardcoded "Last 30 days" caption to reflect the active range); and
neither CSV export endpoint (`/export/shipments`, `/export/cod-summary`) had any frontend UI or a
`Content-Disposition` header (fixed on the backend first — without it, browsers try to render the CSV
instead of downloading it), closed with a new `ExportReportsCard` (branch + date-range filters, two
download links via plain browser navigation to a BFF passthrough route) gated on `report.export`. See
`docs/superpowers/specs/2026-09-01-frontend-phase-f8-reports-analytics-design.md` for full detail.
```

- [ ] **Step 8: Commit**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "docs: mark Frontend Phase F8 (Reports & Analytics) complete"
```

---

## Final Step: Whole-branch review

Once all 6 tasks are committed, review the whole branch against this plan and the spec before merging — matching every prior phase's practice of a final review pass catching cross-task defects invisible to any single task's own review. Pay particular attention to:
- `DashboardPage`'s loading/error/data conditional structure after Task 4's restructuring — confirm the date filter row is genuinely visible during loading and error states, not accidentally hidden by a stray early `return`.
- Every UTC-anchoring call site (Task 4's dashboard filter, Task 5's export filter) actually applies `T00:00:00Z`/`T23:59:59Z` — a missed one would silently reintroduce Frontend Phase F7's exact bug.
- `ExportReportsCard`'s permission gate returning `null` outright (not just visually hidden) when `report.export` is absent, matching this codebase's established `canCreate`/`canUpdate` pattern.

