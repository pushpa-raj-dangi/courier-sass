# Trial-Expired Payment Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every staff user (not just the OrganizationOwner) a proactive, actionable view of their organization's trial/paywall state, and turn `/billing`'s reactive `TrialExpired` message into a clear paywall.

**Architecture:** The backend trial-block (`TrialEnforcementMiddleware`, 402 `TRIAL_EXPIRED`) and the owner-only `/billing` plan-picker already exist and are unmodified. This plan adds (1) a new low-privilege `GET /api/v1/billing/status` endpoint so any authenticated staff member — not just the permission-holder of `org.billing.manage` — can read `{status, trialEndsAt}`; (2) a `TrialStatusBanner` shown app-wide via `AppShell`, driven by that endpoint, that branches its call-to-action on whether the viewer holds `org.billing.manage`; and (3) a permission gate plus a clearer destructive `Alert` on the existing `/billing` page so a non-owner redirected there sees an actionable message instead of a broken error state.

**Tech Stack:** ASP.NET Core minimal APIs (C#), EF Core, Next.js App Router route handlers, TanStack Query, shadcn/ui, Vitest + Testing Library, xUnit.

**Spec:** No separate spec file — this is a bounded task (existing flow, existing files) per `superpowers:brainstorming`'s bounded path. The agreed design was presented and approved in-conversation; it is restated in full in this plan's task breakdown below, so no other document needs to be read alongside this one.

## Global Constraints

- Never trust tenant/permission signals from the frontend — every read of subscription state resolves the organization server-side via `ICurrentTenantProvider.OrganizationId` (CLAUDE.md §33).
- Authorization is permission-based, never a role-name string comparison (CLAUDE.md §6). The new endpoint requires only authentication (any staff member of *some* org may call it — it is tenant-scoped, not permission-scoped); the existing `/billing/subscription`, `/billing/plan`, `/billing/plans`, `/billing/invoices` endpoints keep their existing `org.billing.manage` requirement, unchanged.
- The new low-privilege endpoint must never return `PlanName`/`MonthlyPriceNpr` — those stay behind `org.billing.manage` on the existing endpoints. Only `Status` and `TrialEndsAt` are exposed.
- UI is built from shadcn/ui components only — no raw HTML primitives for interactive/structured elements (project convention).
- TDD: every task's test is written and confirmed failing before the implementation step.
- Enum values are append-only (`SubscriptionStatus` is not touched by this plan at all — no new statuses are added).

---

## Task 1: Backend — low-privilege `GET /api/v1/billing/status`

**Files:**
- Modify: `src/Modules/Billing/Presentation/BillingEndpoints.cs`
- Test: `tests/Api.Tests/BillingEndpointsTests.cs`

**Interfaces:**
- Consumes: `Courier.Billing.Application.SubscriptionService.GetSubscriptionStatusAsync(Guid organizationId, CancellationToken)` (existing, unmodified) → `SubscriptionStatusInfo?` (`Status`, `TrialEndsAt`, `PlanName`, `MonthlyPriceNpr`).
- Produces: `GET /api/v1/billing/status` → `200 OK` with body `{ status: string, trialEndsAt: string | null }` (new `BillingStatusResponse` record) for any authenticated staff user of the org, or `404` if the org has no `Subscription` row at all. Consumed by Task 2's Next.js proxy route.

- [ ] **Step 1: Write the failing test**

Open `tests/Api.Tests/BillingEndpointsTests.cs`. Add a new private helper method (place it right after the existing `RegisterOrgAsync` method, before the first `[Fact]`) — this factors out the CounterStaff-provisioning block that `GetPlan_NoOrgBillingManagePermission_Returns403` already does inline, so the new test below doesn't duplicate it:

```csharp
    // Shared by tests that need a non-owner staff user to prove an endpoint's permission gate (or
    // lack of one) — provisions a CounterStaff user (holds shipment.create/read/update and
    // pricing.read only, per AdditionalRoleSeeds.cs — notably NOT org.billing.manage) via the
    // real invite/accept-invite/login flow and returns an authenticated HttpClient for that user.
    private async Task<HttpClient> InviteCounterStaffAndLoginAsync(HttpClient ownerClient, AuthResult org)
    {
        var roles = await (await ownerClient.GetAsync("/api/v1/roles")).Content.ReadFromJsonAsync<List<RoleResponse>>();
        var counterStaffRole = roles!.Single(r => r.Name == "CounterStaff");

        var branchResponse = await ownerClient.PostAsJsonAsync("/api/v1/branches", new
        {
            code = $"BR{Guid.NewGuid():N}"[..8],
            name = "Billing Perm Branch",
            type = "Branch",
            provinceId = Courier.Geography.Infrastructure.GeographyIds.ProvinceId("Bagmati"),
            districtId = Courier.Geography.Infrastructure.GeographyIds.DistrictId("Bagmati", "Kathmandu"),
            localLevelId = Courier.Geography.Infrastructure.GeographyIds.LocalLevelId("Bagmati", "Kathmandu", "Kathmandu Metropolitan City"),
            wardNumber = 1,
            addressLine = "Somewhere in Kathmandu"
        });
        branchResponse.EnsureSuccessStatusCode();
        var branchId = (await branchResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var staffEmail = $"staff-{Guid.NewGuid():N}@example.com";
        await ownerClient.PostAsJsonAsync("/api/v1/users/invite", new InviteUserRequest(staffEmail, "Staff Person", null, counterStaffRole.Id, branchId, null));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outboxMessage = await dbContext.Set<OutboxMessage>().IgnoreQueryFilters()
            .Where(m => m.EventType == "user.invited")
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync(m => m.Payload.Contains(staffEmail));
        var payload = JsonDocument.Parse(outboxMessage.Payload).RootElement;
        var inviteUrl = payload.GetProperty("InviteUrl").GetString()!;
        var token = Uri.UnescapeDataString(inviteUrl.Split("token=")[1]);
        await ownerClient.PostAsJsonAsync("/api/v1/auth/accept-invite", new AcceptInviteRequest(token, "St@ffPassword1"));

        var staffClient = _factory.CreateClient();
        var loginResponse = await staffClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(staffEmail, "St@ffPassword1"));
        var staffAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResult>();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAuth!.AccessToken);
        return staffClient;
    }
```

Then add this new test at the end of the class, right before the final closing `}`:

```csharp
    [Fact]
    public async Task GetStatus_AsNonOwnerStaff_ReturnsTrialStatusWithoutRequiringBillingPermission()
    {
        var client = _factory.CreateClient();
        var org = await RegisterOrgAsync(client, "trialstatuslite");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", org.AccessToken);
        var staffClient = await InviteCounterStaffAndLoginAsync(client, org);

        var response = await staffClient.GetAsync("/api/v1/billing/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Trial", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("trialEndsAt").ValueKind != JsonValueKind.Null);
        Assert.False(body.TryGetProperty("planName", out _));
        Assert.False(body.TryGetProperty("monthlyPriceNpr", out _));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter GetStatus_AsNonOwnerStaff_ReturnsTrialStatusWithoutRequiringBillingPermission`
Expected: FAIL — `/api/v1/billing/status` isn't mapped yet, so the request 404s and `Assert.Equal(HttpStatusCode.OK, ...)` fails.

- [ ] **Step 3: Implement the endpoint**

In `src/Modules/Billing/Presentation/BillingEndpoints.cs`, add a new record next to the existing `PayInvoiceRequest`/`AssignPlanRequest` records at the top of the file:

```csharp
public record BillingStatusResponse(SubscriptionStatus Status, DateTimeOffset? TrialEndsAt);
```

Then add a new route inside `MapBillingEndpoints`, directly after the existing `GET /subscription` block and before `GET /plans`:

```csharp
        // Deliberately NOT permission-gated beyond authentication: every staff member (not just
        // the OrganizationOwner who holds org.billing.manage) needs to know their org's trial/
        // subscription state to understand why writes might be blocked
        // (TrialEnforcementMiddleware). This is a narrower projection of GET /subscription above
        // (Status/TrialEndsAt only, no PlanName/MonthlyPriceNpr) so it never leaks pricing to
        // non-owner staff.
        group.MapGet("/status", async (SubscriptionService service, ICurrentTenantProvider tenantProvider, CancellationToken cancellationToken) =>
        {
            var status = await service.GetSubscriptionStatusAsync(tenantProvider.OrganizationId!.Value, cancellationToken);
            return status is null
                ? Results.NotFound()
                : Results.Ok(new BillingStatusResponse(status.Status, status.TrialEndsAt));
        }).RequireAuthorization();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj --filter GetStatus_AsNonOwnerStaff_ReturnsTrialStatusWithoutRequiringBillingPermission`
Expected: PASS

- [ ] **Step 5: Run the full Billing/Api test suites to confirm no regressions**

Run: `dotnet test tests/Api.Tests/Api.Tests.csproj && dotnet test tests/Billing.Tests/Billing.Tests.csproj`
Expected: PASS (all tests, including the pre-existing `GetPlan_NoOrgBillingManagePermission_Returns403` and the other three tests in `BillingEndpointsTests.cs`, still green)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Billing/Presentation/BillingEndpoints.cs tests/Api.Tests/BillingEndpointsTests.cs
git commit -m "feat(billing): add low-privilege GET /billing/status for non-owner staff"
```

---

## Task 2: Frontend — Next.js proxy route for the new endpoint

**Files:**
- Create: `src/web/src/app/api/billing/status/route.ts`
- Test: `src/web/src/app/api/billing/status/route.test.ts`

**Interfaces:**
- Consumes: `apiFetch(path: string): Promise<Response>` from `@/lib/auth/apiFetch` (existing, unmodified — forwards the staff session's auth to the backend).
- Produces: `GET /api/billing/status` (Next.js route handler) → proxies to `GET /api/v1/billing/status` from Task 1, returning the same status code and JSON body, or `401` if there's no session. Consumed by Task 3's `useTrialStatus` hook.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/app/api/billing/status/route.test.ts`:

```ts
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/billing/status", () => {
  it("proxies to the backend status endpoint", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ status: "Trial", trialEndsAt: "2026-09-20T00:00:00Z" }),
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/billing/status");
    expect(response.status).toBe(200);
  });

  it("returns 401 when the session is missing", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/app/api/billing/status/route.test.ts`
Expected: FAIL — `./route` does not exist yet (module resolution error).

- [ ] **Step 3: Implement the route handler**

Create `src/web/src/app/api/billing/status/route.ts`:

```ts
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/billing/status");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/web && npx vitest run src/app/api/billing/status/route.test.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/api/billing/status/route.ts src/web/src/app/api/billing/status/route.test.ts
git commit -m "feat(web): proxy the low-privilege billing status endpoint"
```

---

## Task 3: Frontend — `TrialStatusBanner` shown app-wide

**Files:**
- Modify: `src/web/src/features/billing/types.ts`
- Create: `src/web/src/features/billing/hooks/useTrialStatus.ts`
- Create: `src/web/src/components/layout/TrialStatusBanner.tsx`
- Test: `src/web/src/components/layout/TrialStatusBanner.test.tsx`
- Modify: `src/web/src/components/layout/AppShell.tsx`

**Interfaces:**
- Consumes: `clientFetch<T>(path: string): Promise<T>` from `@/lib/api/clientFetch` (existing); `usePermission(permission: string): boolean` from `@/features/auth/hooks/usePermission` (existing, unmodified); `GET /api/billing/status` from Task 2.
- Produces: `TrialStatusInfo` type (`{ status: "Active"|"PastDue"|"Cancelled"|"Trial"|"TrialExpired", trialEndsAt: string | null }`); `useTrialStatus(): UseQueryResult<TrialStatusInfo>`; `<TrialStatusBanner />` component, mounted in `AppShell`.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/components/layout/TrialStatusBanner.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const usePermission = vi.fn();
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: (...args: unknown[]) => usePermission(...args),
}));

const useTrialStatus = vi.fn();
vi.mock("@/features/billing/hooks/useTrialStatus", () => ({
  useTrialStatus: () => useTrialStatus(),
}));

import { TrialStatusBanner } from "./TrialStatusBanner";

describe("TrialStatusBanner", () => {
  it("renders nothing when there is no subscription data yet", () => {
    usePermission.mockReturnValue(true);
    useTrialStatus.mockReturnValue({ data: undefined });

    const { container } = render(<TrialStatusBanner />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when the subscription is Active", () => {
    usePermission.mockReturnValue(true);
    useTrialStatus.mockReturnValue({ data: { status: "Active", trialEndsAt: null } });

    const { container } = render(<TrialStatusBanner />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows a trial countdown with a Choose a plan link for an OrganizationOwner", () => {
    usePermission.mockReturnValue(true);
    const trialEndsAt = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString();
    useTrialStatus.mockReturnValue({ data: { status: "Trial", trialEndsAt } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/3 days left/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /choose a plan/i })).toHaveAttribute("href", "/billing");
  });

  it("shows a trial countdown without a link for non-owner staff", () => {
    usePermission.mockReturnValue(false);
    const trialEndsAt = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString();
    useTrialStatus.mockReturnValue({ data: { status: "Trial", trialEndsAt } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/3 days left/)).toBeInTheDocument();
    expect(screen.getByText(/ask your organization owner/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /choose a plan/i })).not.toBeInTheDocument();
  });

  it("shows the expired message with a Choose a plan link for an OrganizationOwner", () => {
    usePermission.mockReturnValue(true);
    useTrialStatus.mockReturnValue({ data: { status: "TrialExpired", trialEndsAt: null } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/your trial has ended/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /choose a plan/i })).toHaveAttribute("href", "/billing");
  });

  it("shows the expired message without a link for non-owner staff", () => {
    usePermission.mockReturnValue(false);
    useTrialStatus.mockReturnValue({ data: { status: "TrialExpired", trialEndsAt: null } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/your trial has ended/i)).toBeInTheDocument();
    expect(screen.getByText(/ask your organization owner/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /choose a plan/i })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd src/web && npx vitest run src/components/layout/TrialStatusBanner.test.tsx`
Expected: FAIL — `./TrialStatusBanner` does not exist yet.

- [ ] **Step 3: Add the `TrialStatusInfo` type**

In `src/web/src/features/billing/types.ts`, add at the end of the file:

```ts
export interface TrialStatusInfo {
  status: "Active" | "PastDue" | "Cancelled" | "Trial" | "TrialExpired";
  trialEndsAt: string | null;
}
```

- [ ] **Step 4: Add the `useTrialStatus` hook**

Create `src/web/src/features/billing/hooks/useTrialStatus.ts`:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { TrialStatusInfo } from "@/features/billing/types";

export function useTrialStatus() {
  return useQuery({
    queryKey: ["billing-status"],
    queryFn: () => clientFetch<TrialStatusInfo>("/api/billing/status"),
  });
}
```

- [ ] **Step 5: Implement `TrialStatusBanner`**

Create `src/web/src/components/layout/TrialStatusBanner.tsx`:

```tsx
"use client";

import Link from "next/link";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useTrialStatus } from "@/features/billing/hooks/useTrialStatus";

function daysLeft(trialEndsAt: string): number {
  const ms = new Date(trialEndsAt).getTime() - Date.now();
  return Math.max(0, Math.ceil(ms / (1000 * 60 * 60 * 24)));
}

// Amber/destructive inline colors here are deliberate and independent of the app's theme
// tokens, same reasoning as ImpersonationBanner — a paywall banner needs to read as an
// unmistakable warning in both light and dark mode.
export function TrialStatusBanner() {
  const canManageBilling = usePermission("org.billing.manage");
  const { data: status } = useTrialStatus();

  if (!status || status.status === "Active" || status.status === "PastDue" || status.status === "Cancelled") {
    return null;
  }

  const expired = status.status === "TrialExpired";
  const bannerClass = expired
    ? "flex items-center justify-between gap-3 bg-destructive px-4 py-2 text-sm font-medium text-destructive-foreground"
    : "flex items-center justify-between gap-3 bg-amber-500 px-4 py-2 text-sm font-medium text-amber-950";

  const days = status.trialEndsAt ? daysLeft(status.trialEndsAt) : null;
  const message = expired
    ? "Your trial has ended — some actions are blocked until a plan is chosen."
    : days !== null
      ? `You're on a free trial — ${days} day${days === 1 ? "" : "s"} left.`
      : "You're on a free trial.";

  return (
    <div className={bannerClass}>
      <span>{message}</span>
      {canManageBilling ? (
        <Link href="/billing" className="shrink-0 underline underline-offset-2">
          Choose a plan
        </Link>
      ) : (
        <span className="shrink-0 text-xs opacity-80">Ask your organization owner to choose a plan.</span>
      )}
    </div>
  );
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd src/web && npx vitest run src/components/layout/TrialStatusBanner.test.tsx`
Expected: PASS

- [ ] **Step 7: Wire the banner into `AppShell`**

In `src/web/src/components/layout/AppShell.tsx`, add the import next to the existing `ImpersonationBanner` import:

```tsx
import { TrialStatusBanner } from "@/components/layout/TrialStatusBanner";
```

And render it right after `<ImpersonationBanner />`:

```tsx
        <ImpersonationBanner />
        <TrialStatusBanner />
```

- [ ] **Step 8: Run the full frontend test suite and type-check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit`
Expected: PASS, no type errors

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/billing/types.ts src/web/src/features/billing/hooks/useTrialStatus.ts src/web/src/components/layout/TrialStatusBanner.tsx src/web/src/components/layout/TrialStatusBanner.test.tsx src/web/src/components/layout/AppShell.tsx
git commit -m "feat(web): show a proactive trial/paywall banner to all staff"
```

---

## Task 4: Frontend — `/billing` permission gate + clearer `TrialExpired` paywall

**Files:**
- Create: `src/web/src/components/ui/alert.tsx`
- Modify: `src/web/src/features/billing/hooks/useSubscriptionStatus.ts`
- Modify: `src/web/src/features/billing/hooks/useBillingInvoices.ts`
- Modify: `src/web/src/app/(staff)/billing/page.tsx`
- Test: `src/web/src/app/(staff)/billing/page.test.tsx`

**Interfaces:**
- Consumes: `usePermission("org.billing.manage"): boolean` (existing); `ErrorState({ title, description, onRetry? })` from `@/components/states/ErrorState` (existing, unmodified).
- Produces: `Alert`, `AlertTitle`, `AlertDescription` (new shadcn/ui primitives, `new-york` style matching this repo's existing `card.tsx`/`badge.tsx`); `useSubscriptionStatus(options?: { enabled?: boolean })` and `useBillingInvoices(options?: { enabled?: boolean })` (extended, backward-compatible — existing no-arg call sites keep working since `enabled: undefined` behaves as `true`).

- [ ] **Step 1: Add the `Alert` shadcn/ui primitive**

This is a pure presentational primitive with no test file, matching the existing convention for every other file in `src/web/src/components/ui/` (e.g. `badge.tsx`, `card.tsx` — none have a `.test.tsx`).

Create `src/web/src/components/ui/alert.tsx`:

```tsx
import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"

import { cn } from "@/lib/utils"

const alertVariants = cva(
  "relative w-full rounded-lg border px-4 py-3 text-sm [&>svg+div]:translate-y-[-3px] [&>svg]:absolute [&>svg]:left-4 [&>svg]:top-4 [&>svg]:text-foreground [&>svg~*]:pl-7",
  {
    variants: {
      variant: {
        default: "bg-background text-foreground",
        destructive:
          "border-destructive/50 text-destructive dark:border-destructive [&>svg]:text-destructive",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

const Alert = React.forwardRef<
  HTMLDivElement,
  React.HTMLAttributes<HTMLDivElement> & VariantProps<typeof alertVariants>
>(({ className, variant, ...props }, ref) => (
  <div
    ref={ref}
    role="alert"
    className={cn(alertVariants({ variant }), className)}
    {...props}
  />
))
Alert.displayName = "Alert"

const AlertTitle = React.forwardRef<
  HTMLParagraphElement,
  React.HTMLAttributes<HTMLHeadingElement>
>(({ className, ...props }, ref) => (
  <h5
    ref={ref}
    className={cn("mb-1 font-medium leading-none tracking-tight", className)}
    {...props}
  />
))
AlertTitle.displayName = "AlertTitle"

const AlertDescription = React.forwardRef<
  HTMLParagraphElement,
  React.HTMLAttributes<HTMLDivElement>
>(({ className, ...props }, ref) => (
  <div
    ref={ref}
    className={cn("text-sm [&_p]:leading-relaxed", className)}
    {...props}
  />
))
AlertDescription.displayName = "AlertDescription"

export { Alert, AlertTitle, AlertDescription }
```

- [ ] **Step 2: Write the failing test for the page changes**

Create `src/web/src/app/(staff)/billing/page.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const usePermission = vi.fn();
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: (...args: unknown[]) => usePermission(...args),
}));

const useSubscriptionStatus = vi.fn();
vi.mock("@/features/billing/hooks/useSubscriptionStatus", () => ({
  useSubscriptionStatus: () => useSubscriptionStatus(),
}));

const useBillingInvoices = vi.fn();
vi.mock("@/features/billing/hooks/useBillingInvoices", () => ({
  useBillingInvoices: () => useBillingInvoices(),
}));

vi.mock("@/features/billing/hooks/usePayInvoice", () => ({
  usePayInvoice: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const useAvailablePlans = vi.fn();
vi.mock("@/features/billing/hooks/useAvailablePlans", () => ({
  useAvailablePlans: () => useAvailablePlans(),
}));

vi.mock("@/features/billing/hooks/useAssignPlan", () => ({
  useAssignPlan: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

import BillingPage from "./page";

describe("BillingPage", () => {
  it("shows a non-owner message instead of billing data when the user lacks org.billing.manage", () => {
    usePermission.mockReturnValue(false);
    useSubscriptionStatus.mockReturnValue({ data: undefined, isLoading: false });
    useBillingInvoices.mockReturnValue({ data: undefined, isLoading: false, isError: false, refetch: vi.fn() });
    useAvailablePlans.mockReturnValue({ data: undefined, isLoading: false });

    render(<BillingPage />);

    expect(screen.getByText(/billing is managed by your organization owner/i)).toBeInTheDocument();
    expect(screen.queryByText(/choose this plan/i)).not.toBeInTheDocument();
  });

  it("shows a destructive alert and the plan picker when the trial has expired, for an owner", () => {
    usePermission.mockReturnValue(true);
    useSubscriptionStatus.mockReturnValue({
      data: { status: "TrialExpired", trialEndsAt: null, planName: null, monthlyPriceNpr: null },
      isLoading: false,
    });
    useBillingInvoices.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn() });
    useAvailablePlans.mockReturnValue({
      data: [{ id: "p1", name: "Growth", slug: "growth", monthlyPriceNpr: 1500, maxBranches: null, maxStaffUsers: null, maxMonthlyShipments: null, isDefault: true, isActive: true }],
      isLoading: false,
    });

    render(<BillingPage />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent(/your trial has ended/i);
    expect(alert).toHaveTextContent(/choose a plan below to continue/i);
    expect(screen.getByRole("button", { name: /choose this plan/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `cd src/web && npx vitest run "src/app/(staff)/billing/page.test.tsx"`
Expected: FAIL on both tests — the first because the current page has no `org.billing.manage` gate, so "billing is managed by your organization owner" is never rendered (the mocked `useSubscriptionStatus`/`useBillingInvoices` returning `undefined` data instead falls through to a loading/empty render); the second because `screen.getByRole("alert")` finds nothing — today's `TrialExpired` message is a plain `<p>` with no `role="alert"`, which only the new shadcn `Alert` component (Step 5) provides.

- [ ] **Step 4: Add the `enabled` option to the two gated query hooks**

In `src/web/src/features/billing/hooks/useSubscriptionStatus.ts`, replace the full contents with:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { SubscriptionStatusInfo } from "@/features/billing/types";

export function useSubscriptionStatus(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["billing-subscription"],
    queryFn: () => clientFetch<SubscriptionStatusInfo>("/api/billing/subscription"),
    enabled: options?.enabled,
  });
}
```

In `src/web/src/features/billing/hooks/useBillingInvoices.ts`, replace the full contents with:

```ts
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Invoice } from "@/features/billing/types";

export function useBillingInvoices(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["billing-invoices"],
    queryFn: () => clientFetch<Invoice[]>("/api/billing/invoices"),
    enabled: options?.enabled,
  });
}
```

- [ ] **Step 5: Add the permission gate and the `Alert` paywall to the page**

In `src/web/src/app/(staff)/billing/page.tsx`:

Add two imports, next to the existing `@/components/ui/...` imports:

```tsx
import { Alert, AlertTitle, AlertDescription } from "@/components/ui/alert";
```

and, next to the other feature-hook imports:

```tsx
import { usePermission } from "@/features/auth/hooks/usePermission";
```

Change the start of `BillingPage` from:

```tsx
export default function BillingPage() {
  const { data: subscription, isLoading: subscriptionLoading } = useSubscriptionStatus();
  const { data: invoices, isLoading: invoicesLoading, isError, refetch } = useBillingInvoices();
```

to:

```tsx
export default function BillingPage() {
  // usePermission reads from the cached session and returns false until that session loads —
  // the same "hidden/denied until loaded" convention NAV_ITEMS filtering already relies on, so
  // an OrganizationOwner may see this gate flash briefly before their session resolves.
  const canManageBilling = usePermission("org.billing.manage");
  const { data: subscription, isLoading: subscriptionLoading } = useSubscriptionStatus({ enabled: canManageBilling });
  const { data: invoices, isLoading: invoicesLoading, isError, refetch } = useBillingInvoices({ enabled: canManageBilling });
```

Add the gate right after the `columns` array definition, before the existing `if (subscriptionLoading || invoicesLoading)` block:

```tsx
  if (!canManageBilling) {
    return (
      <ErrorState
        title="Billing is managed by your organization owner"
        description="Ask them to sign in and choose a plan to continue."
      />
    );
  }

```

Replace the existing `TrialExpired` paragraph:

```tsx
        {subscription?.status === "TrialExpired" && (
          <p className="mt-1 text-sm text-destructive">Your trial has ended. Choose a plan below to continue using the app.</p>
        )}
```

with:

```tsx
        {subscription?.status === "TrialExpired" && (
          <Alert variant="destructive" className="mt-4">
            <AlertTitle>Your trial has ended</AlertTitle>
            <AlertDescription>Choose a plan below to continue using the app.</AlertDescription>
          </Alert>
        )}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `cd src/web && npx vitest run "src/app/(staff)/billing/page.test.tsx"`
Expected: PASS

- [ ] **Step 7: Run the full frontend test suite and type-check**

Run: `cd src/web && npx vitest run && npx tsc --noEmit`
Expected: PASS, no type errors, no regressions in any other test (in particular `GatewayRedirectForm.test.ts` and every hook/component that already consumed `useSubscriptionStatus`/`useBillingInvoices` with no arguments)

- [ ] **Step 8: Commit**

```bash
git add src/web/src/components/ui/alert.tsx src/web/src/features/billing/hooks/useSubscriptionStatus.ts src/web/src/features/billing/hooks/useBillingInvoices.ts "src/web/src/app/(staff)/billing/page.tsx" "src/web/src/app/(staff)/billing/page.test.tsx"
git commit -m "fix(web): gate /billing on org.billing.manage and clarify the trial-expired paywall"
```

---

## Manual verification (after all 4 tasks)

1. `docker compose up` (or however the local stack is already running — reuse the shared `courier-postgres-1` container, do not start a second one).
2. Register a fresh organization, confirm `TrialStatusBanner` shows "N days left" with a working "Choose a plan" link.
3. In the backend, force that org's `Subscription.TrialEndsAt` into the past and run `TrialExpiryJob` (or wait for its schedule), reload the app: confirm the banner turns red/destructive and `/billing` shows the new `Alert` paywall.
4. Invite a CounterStaff user, log in as them: confirm the banner still shows (with "Ask your organization owner..." instead of a link), and that navigating directly to `/billing` shows the new "Billing is managed by your organization owner" message instead of a broken error state.
5. As the owner, choose a plan from the paywall: confirm the banner disappears and writes (e.g. creating a shipment) succeed again.
