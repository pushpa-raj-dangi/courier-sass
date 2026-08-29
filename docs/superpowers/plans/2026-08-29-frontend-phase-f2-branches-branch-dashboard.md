# Frontend Phase F2 — Branches + Branch Dashboard — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `Branches` feature area (list, create, detail with a read-only record + editable delivery zone, and the branch operational dashboard) and wire the Phase F0 `BranchSwitcher` to real navigation.

**Architecture:** Same shape as every prior phase — thin BFF proxy routes over F0's `apiFetch`, TanStack Query hooks via `clientFetch`, `Card`-based content matching the established icon-badge/subtitle language. One new primitive this phase: a hand-authored `Select` (the shadcn CLI's interactive library prompt still can't be answered in this environment, same issue Phase F1 hit with `Dialog`).

**Tech Stack:** Same as Phases F0/F1, plus `@radix-ui/react-select` (new dependency, plain `npm install` — no CLI involved).

**Spec:** `docs/superpowers/specs/2026-08-29-frontend-phase-f2-branches-branch-dashboard-design.md`. Also see `docs/CLARIFICATIONS.md` #F2.1 (no generic branch-update endpoint — this plan builds read-only detail + delivery-zone-only editing, nothing more) and `docs/superpowers/specs/2026-08-29-frontend-roadmap-design.md` (still-binding Phase F0 conventions).

## Global Constraints

* Branch detail renders every `BranchResponse` field read-only except delivery zone (spec §1, §6) — no form fields, no submit button, for anything else. Do not build toward a `PUT /api/v1/branches/{id}` that doesn't exist.
* The branch dashboard's "Riders" section is an aggregate `{riderCount, totalOpenTasks}` stat, never a named per-rider list (spec §1, §7).
* `GET /api/v1/branches` is already server-side filtered to the caller's accessible branches — no client-side re-filtering by branch scope.
* Every client-side data hook uses `clientFetch` (Phase F1), never a raw `fetch`.
* Color is reserved for genuinely urgent figures (open NDRs use `status-ndr`); missing-shipment and rider-load tiles use neutral `muted` badges (spec §7) — don't invent new colored badges for non-status data.

---

### Task 1: `Select` primitive

**Files:**
- Create: `src/web/src/components/ui/select.tsx`

**Interfaces:**
- Produces: `Select`, `SelectTrigger`, `SelectValue`, `SelectContent`, `SelectItem`, `SelectLabel`, `SelectSeparator` — the standard shadcn `Select` API surface. Every later task in this plan that needs a dropdown selector (`GeographyPicker`'s three cascading selects, branch type, parent branch, delivery zone) imports from here.

- [ ] **Step 1: Install the Radix dependency**

```bash
cd src/web
npm install @radix-ui/react-select
```

- [ ] **Step 2: Author the component**

Create `src/web/src/components/ui/select.tsx`, matching this repo's existing Radix-based primitives
(`dialog.tsx`, `dropdown-menu.tsx`) for styling conventions — tokens, `cn()`, `forwardRef` pattern:

```typescript
"use client"

import * as React from "react"
import * as SelectPrimitive from "@radix-ui/react-select"
import { Check, ChevronDown, ChevronUp } from "lucide-react"

import { cn } from "@/lib/utils"

const Select = SelectPrimitive.Root
const SelectGroup = SelectPrimitive.Group
const SelectValue = SelectPrimitive.Value

const SelectTrigger = React.forwardRef<
  React.ElementRef<typeof SelectPrimitive.Trigger>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.Trigger>
>(({ className, children, ...props }, ref) => (
  <SelectPrimitive.Trigger
    ref={ref}
    className={cn(
      "flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-2 text-sm shadow-sm placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-ring disabled:cursor-not-allowed disabled:opacity-50 [&>span]:line-clamp-1",
      className
    )}
    {...props}
  >
    {children}
    <SelectPrimitive.Icon asChild>
      <ChevronDown className="h-4 w-4 opacity-50" />
    </SelectPrimitive.Icon>
  </SelectPrimitive.Trigger>
))
SelectTrigger.displayName = SelectPrimitive.Trigger.displayName

const SelectScrollUpButton = React.forwardRef<
  React.ElementRef<typeof SelectPrimitive.ScrollUpButton>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.ScrollUpButton>
>(({ className, ...props }, ref) => (
  <SelectPrimitive.ScrollUpButton
    ref={ref}
    className={cn("flex cursor-default items-center justify-center py-1", className)}
    {...props}
  >
    <ChevronUp className="h-4 w-4" />
  </SelectPrimitive.ScrollUpButton>
))
SelectScrollUpButton.displayName = SelectPrimitive.ScrollUpButton.displayName

const SelectScrollDownButton = React.forwardRef<
  React.ElementRef<typeof SelectPrimitive.ScrollDownButton>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.ScrollDownButton>
>(({ className, ...props }, ref) => (
  <SelectPrimitive.ScrollDownButton
    ref={ref}
    className={cn("flex cursor-default items-center justify-center py-1", className)}
    {...props}
  >
    <ChevronDown className="h-4 w-4" />
  </SelectPrimitive.ScrollDownButton>
))
SelectScrollDownButton.displayName = SelectPrimitive.ScrollDownButton.displayName

const SelectContent = React.forwardRef<
  React.ElementRef<typeof SelectPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.Content>
>(({ className, children, position = "popper", ...props }, ref) => (
  <SelectPrimitive.Portal>
    <SelectPrimitive.Content
      ref={ref}
      className={cn(
        "relative z-50 max-h-96 min-w-[8rem] overflow-hidden rounded-md border border-border bg-popover text-popover-foreground shadow-md data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95",
        position === "popper" &&
          "data-[side=bottom]:translate-y-1 data-[side=left]:-translate-x-1 data-[side=right]:translate-x-1 data-[side=top]:-translate-y-1",
        className
      )}
      position={position}
      {...props}
    >
      <SelectScrollUpButton />
      <SelectPrimitive.Viewport
        className={cn(
          "p-1",
          position === "popper" &&
            "h-[var(--radix-select-trigger-height)] w-full min-w-[var(--radix-select-trigger-width)]"
        )}
      >
        {children}
      </SelectPrimitive.Viewport>
      <SelectScrollDownButton />
    </SelectPrimitive.Content>
  </SelectPrimitive.Portal>
))
SelectContent.displayName = SelectPrimitive.Content.displayName

const SelectLabel = React.forwardRef<
  React.ElementRef<typeof SelectPrimitive.Label>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.Label>
>(({ className, ...props }, ref) => (
  <SelectPrimitive.Label
    ref={ref}
    className={cn("px-2 py-1.5 text-sm font-semibold", className)}
    {...props}
  />
))
SelectLabel.displayName = SelectPrimitive.Label.displayName

const SelectItem = React.forwardRef<
  React.ElementRef<typeof SelectPrimitive.Item>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.Item>
>(({ className, children, ...props }, ref) => (
  <SelectPrimitive.Item
    ref={ref}
    className={cn(
      "relative flex w-full cursor-default select-none items-center rounded-sm py-1.5 pl-8 pr-2 text-sm outline-none focus:bg-accent focus:text-accent-foreground data-[disabled]:pointer-events-none data-[disabled]:opacity-50",
      className
    )}
    {...props}
  >
    <span className="absolute left-2 flex h-3.5 w-3.5 items-center justify-center">
      <SelectPrimitive.ItemIndicator>
        <Check className="h-4 w-4" />
      </SelectPrimitive.ItemIndicator>
    </span>
    <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
  </SelectPrimitive.Item>
))
SelectItem.displayName = SelectPrimitive.Item.displayName

const SelectSeparator = React.forwardRef<
  React.ElementRef<typeof SelectPrimitive.Separator>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.Separator>
>(({ className, ...props }, ref) => (
  <SelectPrimitive.Separator
    ref={ref}
    className={cn("-mx-1 my-1 h-px bg-muted", className)}
    {...props}
  />
))
SelectSeparator.displayName = SelectPrimitive.Separator.displayName

export {
  Select,
  SelectGroup,
  SelectValue,
  SelectTrigger,
  SelectContent,
  SelectLabel,
  SelectItem,
  SelectSeparator,
  SelectScrollUpButton,
  SelectScrollDownButton,
}
```

- [ ] **Step 3: Verify the app still builds**

Run: `cd src/web && npm run build`
Expected: build succeeds (no consumer yet — this task is plumbing only).

- [ ] **Step 4: Commit**

```bash
git add src/web/src/components/ui/select.tsx src/web/package.json src/web/package-lock.json
git commit -m "feat(web): add hand-authored Select primitive (@radix-ui/react-select)"
```

---

### Task 2: Geography BFF routes + hooks

**Files:**
- Create: `src/web/src/features/geography/types.ts`
- Create: `src/web/src/app/api/geography/provinces/route.ts`
- Create: `src/web/src/app/api/geography/districts/route.ts`
- Create: `src/web/src/app/api/geography/local-levels/route.ts`
- Create: `src/web/src/app/api/geography/wards/route.ts`
- Create: `src/web/src/features/geography/hooks/useGeography.ts`
- Test: `src/web/src/app/api/geography/districts/route.test.ts`

**Interfaces:**
- Consumes: `apiFetch` (Phase F0), `clientFetch` (Phase F1).
- Produces: `Province { id, name }`, `District { id, provinceId, name }`,
  `LocalLevel { id, districtId, name, type }`, `Ward { id, localLevelId, number }` types;
  `useProvinces()`, `useDistricts(provinceId: string | null)`,
  `useLocalLevels(districtId: string | null)` — each `enabled: false` (no fetch) while its parent
  id is `null`, matching the cascading-disabled-until-parent-chosen behavior spec §5 describes.
  Task 3's `GeographyPicker` is the consumer.

- [ ] **Step 1: Types**

Create `src/web/src/features/geography/types.ts`:

```typescript
export interface Province {
  id: string;
  name: string;
}

export interface District {
  id: string;
  provinceId: string;
  name: string;
}

export type LocalLevelType = "Metropolitan" | "SubMetropolitan" | "Municipality" | "RuralMunicipality";

export interface LocalLevel {
  id: string;
  districtId: string;
  name: string;
  type: LocalLevelType;
}

export interface Ward {
  id: string;
  localLevelId: string;
  number: number;
}
```

- [ ] **Step 2: BFF routes**

Create `src/web/src/app/api/geography/provinces/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/geography/provinces");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/geography/districts/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const provinceId = request.nextUrl.searchParams.get("provinceId");
  if (!provinceId) {
    return NextResponse.json({ error: "provinceId is required" }, { status: 400 });
  }
  try {
    const response = await apiFetch(`/api/v1/geography/districts?provinceId=${provinceId}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/geography/local-levels/route.ts` (identical shape, `districtId`):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const districtId = request.nextUrl.searchParams.get("districtId");
  if (!districtId) {
    return NextResponse.json({ error: "districtId is required" }, { status: 400 });
  }
  try {
    const response = await apiFetch(`/api/v1/geography/local-levels?districtId=${districtId}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/geography/wards/route.ts` (identical shape, `localLevelId` — not
consumed by `GeographyPicker` this phase, included now per spec §5 for later address-entry forms):

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const localLevelId = request.nextUrl.searchParams.get("localLevelId");
  if (!localLevelId) {
    return NextResponse.json({ error: "localLevelId is required" }, { status: 400 });
  }
  try {
    const response = await apiFetch(`/api/v1/geography/wards?localLevelId=${localLevelId}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 3: Write the failing test for one representative route**

Create `src/web/src/app/api/geography/districts/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/geography/districts", () => {
  it("returns 400 when provinceId is missing", async () => {
    const request = new NextRequest("http://localhost/api/geography/districts");

    const response = await GET(request);

    expect(response.status).toBe(400);
    expect(apiFetch).not.toHaveBeenCalled();
  });

  it("forwards provinceId to the backend and proxies the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: "d1", provinceId: "p1", name: "Kathmandu" }],
    } as Response);
    const request = new NextRequest("http://localhost/api/geography/districts?provinceId=p1");

    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/geography/districts?provinceId=p1");
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual([{ id: "d1", provinceId: "p1", name: "Kathmandu" }]);
  });
});
```

- [ ] **Step 4: Run the test to verify it fails, then passes**

Run: `cd src/web && npm test -- geography/districts/route.test.ts`
Expected: FAIL first (`./route` doesn't exist until Step 2's file is in place — if you completed
Step 2 already, skip straight to verifying PASS instead, and note that for this task the route was
written before its test; every other task in this plan writes the test first).
Then confirm: PASS (2 tests) once both Step 2 and this file exist.

- [ ] **Step 5: Hooks**

Create `src/web/src/features/geography/hooks/useGeography.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Province, District, LocalLevel } from "@/features/geography/types";

export function useProvinces() {
  return useQuery({
    queryKey: ["geography", "provinces"],
    queryFn: () => clientFetch<Province[]>("/api/geography/provinces"),
  });
}

export function useDistricts(provinceId: string | null) {
  return useQuery({
    queryKey: ["geography", "districts", provinceId],
    queryFn: () => clientFetch<District[]>(`/api/geography/districts?provinceId=${provinceId}`),
    enabled: provinceId !== null,
  });
}

export function useLocalLevels(districtId: string | null) {
  return useQuery({
    queryKey: ["geography", "local-levels", districtId],
    queryFn: () => clientFetch<LocalLevel[]>(`/api/geography/local-levels?districtId=${districtId}`),
    enabled: districtId !== null,
  });
}
```

- [ ] **Step 6: Run the full frontend suite**

Run: `cd src/web && npm test`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/geography
git commit -m "feat(web): add geography BFF routes and cascading-query hooks"
```

---

### Task 3: `GeographyPicker`

**Files:**
- Create: `src/web/src/features/branches/GeographyPicker.tsx`
- Test: `src/web/src/features/branches/GeographyPicker.test.tsx`

**Interfaces:**
- Consumes: `Select*` (Task 1), `useProvinces`/`useDistricts`/`useLocalLevels` (Task 2).
- Produces: `<GeographyPicker value={{ provinceId, districtId, localLevelId, wardNumber }}
  onChange={(next) => void} />`. Task 6's branch create form is the consumer.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/branches/GeographyPicker.test.tsx`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { GeographyPicker } from "./GeographyPicker";

function renderPicker(onChange = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    onChange,
    ...render(
      <QueryClientProvider client={queryClient}>
        <GeographyPicker
          value={{ provinceId: null, districtId: null, localLevelId: null, wardNumber: null }}
          onChange={onChange}
        />
      </QueryClientProvider>
    ),
  };
}

describe("GeographyPicker", () => {
  beforeEach(() => {
    global.fetch = vi.fn((url: string) => {
      if (url.includes("/provinces")) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: async () => [{ id: "p1", name: "Bagmati" }],
        });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => [] });
    }) as unknown as typeof fetch;
  });

  it("disables the District select until a Province is chosen", async () => {
    renderPicker();

    await waitFor(() => expect(screen.getByText("Bagmati")).toBeInTheDocument());
    expect(screen.getByLabelText(/district/i)).toBeDisabled();
  });

  it("disables the Local Level select until a District is chosen", () => {
    renderPicker();

    expect(screen.getByLabelText(/local level/i)).toBeDisabled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- GeographyPicker.test.tsx`
Expected: FAIL — `./GeographyPicker` does not exist yet.

- [ ] **Step 3: Implement**

Create `src/web/src/features/branches/GeographyPicker.tsx`:

```typescript
"use client";

import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useProvinces, useDistricts, useLocalLevels } from "@/features/geography/hooks/useGeography";

export interface GeographyValue {
  provinceId: string | null;
  districtId: string | null;
  localLevelId: string | null;
  wardNumber: number | null;
}

const LOCAL_LEVEL_TYPE_LABEL: Record<string, string> = {
  Metropolitan: "Metropolitan",
  SubMetropolitan: "Sub-Metropolitan",
  Municipality: "Municipality",
  RuralMunicipality: "Rural Municipality",
};

export function GeographyPicker({
  value,
  onChange,
}: {
  value: GeographyValue;
  onChange: (next: GeographyValue) => void;
}) {
  const { data: provinces } = useProvinces();
  const { data: districts } = useDistricts(value.provinceId);
  const { data: localLevels } = useLocalLevels(value.districtId);

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <div className="space-y-2">
        <Label htmlFor="province">Province</Label>
        <Select
          value={value.provinceId ?? undefined}
          onValueChange={(provinceId) =>
            onChange({ provinceId, districtId: null, localLevelId: null, wardNumber: value.wardNumber })
          }
        >
          <SelectTrigger id="province" aria-label="Province">
            <SelectValue placeholder="Select province" />
          </SelectTrigger>
          <SelectContent>
            {provinces?.map((p) => (
              <SelectItem key={p.id} value={p.id}>
                {p.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label htmlFor="district">District</Label>
        <Select
          value={value.districtId ?? undefined}
          onValueChange={(districtId) =>
            onChange({ ...value, districtId, localLevelId: null })
          }
          disabled={value.provinceId === null}
        >
          <SelectTrigger id="district" aria-label="District">
            <SelectValue placeholder="Select district" />
          </SelectTrigger>
          <SelectContent>
            {districts?.map((d) => (
              <SelectItem key={d.id} value={d.id}>
                {d.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label htmlFor="local-level">Local Level</Label>
        <Select
          value={value.localLevelId ?? undefined}
          onValueChange={(localLevelId) => onChange({ ...value, localLevelId })}
          disabled={value.districtId === null}
        >
          <SelectTrigger id="local-level" aria-label="Local level">
            <SelectValue placeholder="Select local level" />
          </SelectTrigger>
          <SelectContent>
            {localLevels?.map((l) => (
              <SelectItem key={l.id} value={l.id}>
                {l.name} ({LOCAL_LEVEL_TYPE_LABEL[l.type]})
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label htmlFor="ward-number">Ward Number</Label>
        <Input
          id="ward-number"
          type="number"
          min={1}
          value={value.wardNumber ?? ""}
          onChange={(e) =>
            onChange({ ...value, wardNumber: e.target.value === "" ? null : Number(e.target.value) })
          }
        />
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- GeographyPicker.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/branches/GeographyPicker.tsx src/web/src/features/branches/GeographyPicker.test.tsx
git commit -m "feat(web): add GeographyPicker cascading Province/District/Local Level selector"
```

---

### Task 4: Branches BFF routes + types

**Files:**
- Create: `src/web/src/features/branches/types.ts`
- Create: `src/web/src/app/api/branches/route.ts`
- Create: `src/web/src/app/api/branches/[id]/route.ts`
- Create: `src/web/src/app/api/branches/[id]/delivery-zone/route.ts`
- Test: `src/web/src/app/api/branches/route.test.ts`

**Interfaces:**
- Consumes: `apiFetch` (Phase F0).
- Produces: `BranchType` union, `Branch` type (mirroring `BranchResponse`), `CreateBranchInput`
  type; `GET/POST /api/branches`, `GET /api/branches/[id]`,
  `PUT /api/branches/[id]/delivery-zone`. Tasks 5-7's hooks are the consumers.

- [ ] **Step 1: Types**

Create `src/web/src/features/branches/types.ts`:

```typescript
export type BranchType = "HeadOffice" | "Hub" | "Branch" | "PickupPoint" | "Warehouse";

export interface Branch {
  id: string;
  code: string;
  name: string;
  type: BranchType;
  provinceId: string;
  districtId: string;
  localLevelId: string;
  wardNumber: number;
  addressLine: string;
  phone: string | null;
  operatingHours: string | null;
  latitude: number | null;
  longitude: number | null;
  parentBranchId: string | null;
  deliveryZoneId: string | null;
  isActive: boolean;
}

export interface CreateBranchInput {
  code: string;
  name: string;
  type: BranchType;
  provinceId: string;
  districtId: string;
  localLevelId: string;
  wardNumber: number;
  addressLine: string;
  phone?: string;
  operatingHours?: string;
  parentBranchId?: string;
}
```

- [ ] **Step 2: Write the failing test**

Create `src/web/src/app/api/branches/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("/api/branches", () => {
  it("GET proxies the branch list", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: "b1", code: "KTM-01", name: "Kathmandu Hub" }],
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/branches");
    expect(response.status).toBe(200);
  });

  it("POST forwards the request body and proxies a validation error", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ error: { code: "BRANCH_VALIDATION_FAILED", message: "Code is required." } }),
    } as Response);
    const request = new Request("http://localhost/api/branches", {
      method: "POST",
      body: JSON.stringify({ code: "" }),
    });

    const response = await POST(request as never);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/branches",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ code: "" }) })
    );
    expect(response.status).toBe(400);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- api/branches/route.test.ts`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 4: Implement**

Create `src/web/src/app/api/branches/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/branches");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/branches", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/branches/[id]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/branches/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/branches/[id]/delivery-zone/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PUT(request: NextRequest, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/branches/${params.id}/delivery-zone`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- api/branches/route.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/branches/types.ts src/web/src/app/api/branches
git commit -m "feat(web): add branches BFF routes (list, create, detail, delivery-zone)"
```

---

### Task 5: Branch list page

**Files:**
- Create: `src/web/src/features/branches/hooks/useBranches.ts`
- Create: `src/web/src/app/(staff)/branches/page.tsx`
- Test: `src/web/src/features/branches/hooks/useBranches.test.tsx`

**Interfaces:**
- Consumes: `DataTable` (Phase F0), `clientFetch` (Phase F1), `Branch` (Task 4).
- Produces: `useBranches(): UseQueryResult<Branch[]>`; the `/branches` route.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/branches/hooks/useBranches.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useBranches } from "./useBranches";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useBranches", () => {
  it("fetches from the BFF proxy route", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: "b1", code: "KTM-01", name: "Kathmandu Hub", isActive: true }],
    });

    const { result } = renderHook(() => useBranches(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].name).toBe("Kathmandu Hub");
    expect(global.fetch).toHaveBeenCalledWith("/api/branches", undefined);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- useBranches.test.tsx`
Expected: FAIL — `./useBranches` does not exist yet.

- [ ] **Step 3: Implement the hook**

Create `src/web/src/features/branches/hooks/useBranches.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/branches/types";

export function useBranches() {
  return useQuery({
    queryKey: ["branches"],
    queryFn: () => clientFetch<Branch[]>("/api/branches"),
  });
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- useBranches.test.tsx`
Expected: PASS.

- [ ] **Step 5: The list page**

Create `src/web/src/app/(staff)/branches/page.tsx`:

```typescript
"use client";

import Link from "next/link";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import type { Branch } from "@/features/branches/types";

const columns: ColumnDef<Branch>[] = [
  { key: "code", header: "Code", render: (b) => b.code },
  {
    key: "name",
    header: "Name",
    render: (b) => (
      <Link href={`/branches/${b.id}`} className="font-medium text-foreground hover:underline">
        {b.name}
      </Link>
    ),
  },
  { key: "type", header: "Type", render: (b) => b.type },
  { key: "active", header: "Active", render: (b) => (b.isActive ? "Yes" : "No") },
];

export default function BranchesPage() {
  const { data, isLoading, isError, refetch } = useBranches();
  const canCreate = usePermission("branch.create");

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load branches"
        description="Something went wrong fetching the branch list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Branches</h1>
        {canCreate && (
          <Button asChild size="sm" className="gap-1.5">
            <Link href="/branches/new">
              <Plus className="h-4 w-4" strokeWidth={2} />
              New Branch
            </Link>
          </Button>
        )}
      </div>
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
    </div>
  );
}
```

Note: `GET /api/v1/branches` returns a plain array, not a paginated envelope — wrapping it in a
single-page `PagedResult`-shaped object at the page level (`page: 1, pageSize: data.length,
totalCount: data.length, totalPages: 1`) reuses `DataTable` without changing its contract.
Pagination controls render but are inert (`page 1 of 1`), which is correct: there is nothing to
paginate against server-side yet.

- [ ] **Step 6: Run the full frontend suite**

Run: `cd src/web && npm test`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/branches/hooks/useBranches.ts src/web/src/features/branches/hooks/useBranches.test.tsx "src/web/src/app/(staff)/branches/page.tsx"
git commit -m "feat(web): add branch list page"
```

---

### Task 6: Branch create form

**Files:**
- Create: `src/web/src/features/branches/schema.ts`
- Create: `src/web/src/features/branches/hooks/useCreateBranch.ts`
- Create: `src/web/src/app/(staff)/branches/new/page.tsx`
- Test: `src/web/src/features/branches/schema.test.ts`

**Interfaces:**
- Consumes: `GeographyPicker` (Task 3), `Select` (Task 1), `useBranches` (Task 5, for the parent-branch
  dropdown and to invalidate after create), `CreateBranchInput` (Task 4).
- Produces: `createBranchSchema` (Zod); `useCreateBranch()` (a `useMutation`). The `/branches/new`
  route.

- [ ] **Step 1: Write the failing test for the schema (the piece with real validation logic)**

Create `src/web/src/features/branches/schema.test.ts`:

```typescript
import { describe, expect, it } from "vitest";
import { createBranchSchema } from "./schema";

describe("createBranchSchema", () => {
  it("accepts a fully-filled valid branch", () => {
    const result = createBranchSchema.safeParse({
      code: "KTM-02",
      name: "Kathmandu Branch",
      type: "Branch",
      provinceId: "p1",
      districtId: "d1",
      localLevelId: "l1",
      wardNumber: 5,
      addressLine: "New Road",
    });
    expect(result.success).toBe(true);
  });

  it("rejects a missing geography chain", () => {
    const result = createBranchSchema.safeParse({
      code: "KTM-02",
      name: "Kathmandu Branch",
      type: "Branch",
      provinceId: null,
      districtId: null,
      localLevelId: null,
      wardNumber: null,
      addressLine: "New Road",
    });
    expect(result.success).toBe(false);
  });

  it("rejects a non-positive ward number", () => {
    const result = createBranchSchema.safeParse({
      code: "KTM-02",
      name: "Kathmandu Branch",
      type: "Branch",
      provinceId: "p1",
      districtId: "d1",
      localLevelId: "l1",
      wardNumber: 0,
      addressLine: "New Road",
    });
    expect(result.success).toBe(false);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- branches/schema.test.ts`
Expected: FAIL — `./schema` does not exist yet.

- [ ] **Step 3: Implement the schema**

Create `src/web/src/features/branches/schema.ts`:

```typescript
import { z } from "zod";

export const createBranchSchema = z.object({
  code: z.string().min(1, "Code is required."),
  name: z.string().min(1, "Name is required."),
  type: z.enum(["HeadOffice", "Hub", "Branch", "PickupPoint", "Warehouse"]),
  provinceId: z.string({ required_error: "Province is required." }).min(1, "Province is required."),
  districtId: z.string({ required_error: "District is required." }).min(1, "District is required."),
  localLevelId: z
    .string({ required_error: "Local level is required." })
    .min(1, "Local level is required."),
  wardNumber: z.coerce.number({ required_error: "Ward number is required." }).positive("Ward number must be positive."),
  addressLine: z.string().min(1, "Address is required."),
  phone: z.string().optional(),
  operatingHours: z.string().optional(),
  parentBranchId: z.string().optional(),
});

export type CreateBranchFormValues = z.infer<typeof createBranchSchema>;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- branches/schema.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: The mutation hook**

Create `src/web/src/features/branches/hooks/useCreateBranch.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch, CreateBranchInput } from "@/features/branches/types";

export function useCreateBranch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateBranchInput) =>
      clientFetch<Branch>("/api/branches", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["branches"] });
    },
  });
}
```

- [ ] **Step 6: Add the shadcn primitives this form needs beyond what already exists**

`Textarea` doesn't exist yet and isn't needed (every field here is a single-line `Input` or
`Select`) — no new primitive beyond Task 1's `Select`.

- [ ] **Step 7: The create page**

Create `src/web/src/app/(staff)/branches/new/page.tsx`:

```typescript
"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import { createBranchSchema, type CreateBranchFormValues } from "@/features/branches/schema";
import { useCreateBranch } from "@/features/branches/hooks/useCreateBranch";
import { useBranches } from "@/features/branches/hooks/useBranches";

const BRANCH_TYPES = ["HeadOffice", "Hub", "Branch", "PickupPoint", "Warehouse"] as const;

export default function NewBranchPage() {
  const router = useRouter();
  const createBranch = useCreateBranch();
  const { data: existingBranches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<CreateBranchFormValues>({ resolver: zodResolver(createBranchSchema) });

  const geographyValue: GeographyValue = {
    provinceId: watch("provinceId") ?? null,
    districtId: watch("districtId") ?? null,
    localLevelId: watch("localLevelId") ?? null,
    wardNumber: watch("wardNumber") ?? null,
  };

  async function onSubmit(values: CreateBranchFormValues) {
    setServerError(null);
    try {
      const created = await createBranch.mutateAsync(values);
      router.push(`/branches/${created.id}`);
    } catch {
      setServerError("Couldn't create the branch. Check the fields and try again.");
    }
  }

  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <CardTitle className="font-display text-lg">New Branch</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="code">Code</Label>
              <Input id="code" {...register("code")} />
              {errors.code && <p className="text-sm text-destructive">{errors.code.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="name">Name</Label>
              <Input id="name" {...register("name")} />
              {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="type">Type</Label>
            <Controller
              name="type"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="type" aria-label="Type">
                    <SelectValue placeholder="Select type" />
                  </SelectTrigger>
                  <SelectContent>
                    {BRANCH_TYPES.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.type && <p className="text-sm text-destructive">{errors.type.message}</p>}
          </div>
          <GeographyPicker
            value={geographyValue}
            onChange={(next) => {
              setValue("provinceId", next.provinceId ?? "");
              setValue("districtId", next.districtId ?? "");
              setValue("localLevelId", next.localLevelId ?? "");
              setValue("wardNumber", next.wardNumber ?? undefined as never);
            }}
          />
          <div className="space-y-2">
            <Label htmlFor="addressLine">Address</Label>
            <Input id="addressLine" {...register("addressLine")} />
            {errors.addressLine && (
              <p className="text-sm text-destructive">{errors.addressLine.message}</p>
            )}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="phone">Phone</Label>
              <Input id="phone" {...register("phone")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="operatingHours">Operating Hours</Label>
              <Input id="operatingHours" placeholder="9:00 AM - 6:00 PM" {...register("operatingHours")} />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="parentBranchId">Parent Branch</Label>
            <Controller
              name="parentBranchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="parentBranchId" aria-label="Parent branch">
                    <SelectValue placeholder="None" />
                  </SelectTrigger>
                  <SelectContent>
                    {existingBranches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>
                        {b.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Branch"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 8: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS / build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/branches/schema.ts src/web/src/features/branches/schema.test.ts src/web/src/features/branches/hooks/useCreateBranch.ts "src/web/src/app/(staff)/branches/new/page.tsx"
git commit -m "feat(web): add branch create form"
```

---

### Task 7: Branch detail page (record + delivery zone)

**Files:**
- Create: `src/web/src/features/branches/hooks/useBranch.ts`
- Create: `src/web/src/features/geography/hooks/useDeliveryZones.ts`
- Create: `src/web/src/app/api/delivery-zones/route.ts`
- Create: `src/web/src/features/branches/hooks/useUpdateDeliveryZone.ts`
- Create: `src/web/src/features/branches/BranchRecord.tsx`
- Create: `src/web/src/app/(staff)/branches/[id]/page.tsx`
- Test: `src/web/src/features/branches/BranchRecord.test.tsx`

**Interfaces:**
- Consumes: `Select` (Task 1), `Branch` (Task 4), `usePermission` (Phase F0).
- Produces: `useBranch(id)`, `useDeliveryZones()`, `useUpdateDeliveryZone(branchId)`;
  `<BranchRecord branch={Branch} />`. Task 10 adds the dashboard section below this on the same
  page.

- [ ] **Step 1: `useBranch` hook**

Create `src/web/src/features/branches/hooks/useBranch.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/branches/types";

export function useBranch(id: string) {
  return useQuery({
    queryKey: ["branches", id],
    queryFn: () => clientFetch<Branch>(`/api/branches/${id}`),
  });
}
```

- [ ] **Step 2: Delivery zone list route + hook**

Create `src/web/src/app/api/delivery-zones/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/delivery-zones");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/features/geography/hooks/useDeliveryZones.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface DeliveryZone {
  id: string;
  name: string;
  coveredAreaIds: string[];
}

export function useDeliveryZones() {
  return useQuery({
    queryKey: ["delivery-zones"],
    queryFn: () => clientFetch<DeliveryZone[]>("/api/delivery-zones"),
  });
}
```

- [ ] **Step 3: Delivery zone update mutation**

Create `src/web/src/features/branches/hooks/useUpdateDeliveryZone.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/branches/types";

export function useUpdateDeliveryZone(branchId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (deliveryZoneId: string) =>
      clientFetch<Branch>(`/api/branches/${branchId}/delivery-zone`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ deliveryZoneId }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["branches", branchId] });
    },
  });
}
```

- [ ] **Step 4: Write the failing test for `BranchRecord`**

Create `src/web/src/features/branches/BranchRecord.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BranchRecord } from "./BranchRecord";
import type { Branch } from "./types";

const branch: Branch = {
  id: "b1",
  code: "KTM-01",
  name: "Kathmandu Hub",
  type: "Hub",
  provinceId: "p1",
  districtId: "d1",
  localLevelId: "l1",
  wardNumber: 5,
  addressLine: "New Road",
  phone: "9800000000",
  operatingHours: "9:00 AM - 6:00 PM",
  latitude: null,
  longitude: null,
  parentBranchId: null,
  deliveryZoneId: null,
  isActive: true,
};

function renderRecord() {
  global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <BranchRecord branch={branch} />
    </QueryClientProvider>
  );
}

describe("BranchRecord", () => {
  it("renders every branch field read-only", () => {
    renderRecord();

    expect(screen.getByText("KTM-01")).toBeInTheDocument();
    expect(screen.getByText("Kathmandu Hub")).toBeInTheDocument();
    expect(screen.getByText("New Road")).toBeInTheDocument();
    // No text input exists for Name/Code/Address - everything but delivery zone is plain text.
    expect(screen.queryByLabelText(/^name$/i)).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `cd src/web && npm test -- BranchRecord.test.tsx`
Expected: FAIL — `./BranchRecord` does not exist yet.

- [ ] **Step 6: Implement `BranchRecord`**

Create `src/web/src/features/branches/BranchRecord.tsx`:

```typescript
"use client";

import { Building2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Label } from "@/components/ui/label";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useDeliveryZones } from "@/features/geography/hooks/useDeliveryZones";
import { useUpdateDeliveryZone } from "@/features/branches/hooks/useUpdateDeliveryZone";
import type { Branch } from "@/features/branches/types";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function BranchRecord({ branch }: { branch: Branch }) {
  const { data: zones } = useDeliveryZones();
  const updateZone = useUpdateDeliveryZone(branch.id);
  const canUpdate = usePermission("branch.update", { branchId: branch.id });

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Building2 className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          {branch.name}
        </CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-4">
        <Field label="Code" value={branch.code} />
        <Field label="Name" value={branch.name} />
        <Field label="Type" value={branch.type} />
        <Field label="Active" value={branch.isActive ? "Yes" : "No"} />
        <Field label="Address" value={branch.addressLine} />
        <Field label="Ward" value={String(branch.wardNumber)} />
        <Field label="Phone" value={branch.phone ?? "—"} />
        <Field label="Operating Hours" value={branch.operatingHours ?? "—"} />
        <div className="col-span-2 flex flex-col gap-1 sm:col-span-4">
          <Label htmlFor="delivery-zone" className="text-xs text-muted-foreground">
            Delivery Zone
          </Label>
          <Select
            value={branch.deliveryZoneId ?? undefined}
            onValueChange={(zoneId) => updateZone.mutate(zoneId)}
            disabled={!canUpdate}
          >
            <SelectTrigger id="delivery-zone" aria-label="Delivery zone" className="max-w-xs">
              <SelectValue placeholder="Not assigned" />
            </SelectTrigger>
            <SelectContent>
              {zones?.map((z) => (
                <SelectItem key={z.id} value={z.id}>
                  {z.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `cd src/web && npm test -- BranchRecord.test.tsx`
Expected: PASS.

- [ ] **Step 8: The detail page (record section only — Task 10 adds the dashboard below)**

Create `src/web/src/app/(staff)/branches/[id]/page.tsx`:

```typescript
"use client";

import { useParams } from "next/navigation";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranch } from "@/features/branches/hooks/useBranch";
import { BranchRecord } from "@/features/branches/BranchRecord";

export default function BranchDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data: branch, isLoading, isError, refetch } = useBranch(id);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError || !branch) {
    return (
      <ErrorState
        title="Couldn't load this branch"
        description="It may not exist, or you may not have access to it."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6">
      <BranchRecord branch={branch} />
    </div>
  );
}
```

- [ ] **Step 9: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS / build succeeds.

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/branches/hooks/useBranch.ts src/web/src/features/branches/hooks/useUpdateDeliveryZone.ts src/web/src/features/geography/hooks/useDeliveryZones.ts src/web/src/app/api/delivery-zones src/web/src/features/branches/BranchRecord.tsx src/web/src/features/branches/BranchRecord.test.tsx "src/web/src/app/(staff)/branches/[id]/page.tsx"
git commit -m "feat(web): add branch detail page (read-only record + delivery zone editor)"
```

---

### Task 8: Branch dashboard BFF route + hook

**Files:**
- Create: `src/web/src/features/reports/types.ts` (modify — add branch dashboard types alongside
  Phase F1's `OrgDashboardSummary`)
- Create: `src/web/src/app/api/reports/dashboard/branch/[id]/route.ts`
- Create: `src/web/src/features/reports/hooks/useBranchDashboard.ts`
- Test: `src/web/src/app/api/reports/dashboard/branch/[id]/route.test.ts`

**Interfaces:**
- Consumes: `apiFetch` (Phase F0), `clientFetch` (Phase F1).
- Produces: `BranchDashboardSummary` type; `GET /api/reports/dashboard/branch/[id]`;
  `useBranchDashboard(branchId): UseQueryResult<BranchDashboardSummary>`. Task 9's dashboard
  components are the consumer.

- [ ] **Step 1: Extend the reports types**

Modify `src/web/src/features/reports/types.ts`, adding (do not change the existing
`OrgDashboardSummary`/`CodFinancialSummary` from Phase F1):

```typescript
export interface BranchShipmentCounts {
  outgoing: number;
  pendingSorting: number;
  pendingDispatch: number;
}

export interface BranchManifestCounts {
  inTransitTowardBranch: number;
  recentlyClosed: number;
}

export interface DeliverySuccessStats {
  attempts: number;
  successes: number;
}

export interface BranchDashboardSummary {
  shipmentCounts: BranchShipmentCounts;
  manifestCounts: BranchManifestCounts;
  missingShipmentExceptions: number;
  riders: Array<{ riderId: string; openTaskCount: number }>;
  todaysDeliveries: DeliverySuccessStats;
  codPendingFinanceVerification: number;
  openNdrCount: number;
}
```

- [ ] **Step 2: Write the failing test**

Create `src/web/src/app/api/reports/dashboard/branch/[id]/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/reports/dashboard/branch/[id]", () => {
  it("proxies the backend response for the given branch id", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ missingShipmentExceptions: 0 }),
    } as Response);

    const response = await GET({} as Request, { params: { id: "b1" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/reports/dashboard/branch/b1");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET({} as Request, { params: { id: "b1" } });

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- dashboard/branch/[id]/route.test.ts`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 4: Implement the route**

Create `src/web/src/app/api/reports/dashboard/branch/[id]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/reports/dashboard/branch/${params.id}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- dashboard/branch/[id]/route.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 6: The hook**

Create `src/web/src/features/reports/hooks/useBranchDashboard.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { BranchDashboardSummary } from "@/features/reports/types";

export function useBranchDashboard(branchId: string) {
  return useQuery({
    queryKey: ["branch-dashboard", branchId],
    queryFn: () => clientFetch<BranchDashboardSummary>(`/api/reports/dashboard/branch/${branchId}`),
  });
}
```

- [ ] **Step 7: Run the full frontend suite**

Run: `cd src/web && npm test`
Expected: PASS, no regressions.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/reports/types.ts src/web/src/app/api/reports/dashboard/branch src/web/src/features/reports/hooks/useBranchDashboard.ts
git commit -m "feat(web): add branch dashboard BFF route and hook"
```

---

### Task 9: Branch dashboard content components

**Files:**
- Create: `src/web/src/features/branches/components/BranchShipments.tsx`
- Create: `src/web/src/features/branches/components/BranchExceptions.tsx`
- Test: `src/web/src/features/branches/components/BranchExceptions.test.tsx`

**Interfaces:**
- Consumes: `BranchDashboardSummary` (Task 8).
- Produces: `<BranchShipments data={{ shipmentCounts, manifestCounts, todaysDeliveries }} />`,
  `<BranchExceptions data={{ missingShipmentExceptions, openNdrCount, riders,
  codPendingFinanceVerification }} />`. Task 10's detail page composes both.

Per spec §7 and §4 of Task 8's plan, invoke the `dataviz` skill for the exact tile treatment before
writing these two components, same division of labor as Phase F1's dashboard tiles — apply its
guidance to the JSX below, keeping the data contract (props, formatting rules, icon/color
assignments) as specified.

- [ ] **Step 1: Invoke the `dataviz` skill**

Ask it for guidance on presenting: (a) two small count groups (Shipments: 3 values, Manifests: 2
values) plus one rate stat (Today's Deliveries), inside one Card; (b) an "exceptions" row mixing an
urgent count (open NDRs, colored) with two neutral operational counts (missing shipments, riders
with open tasks) plus one currency figure — consistent with Phase F1's established stat-tile
contract (proportional figures, color reserved for genuinely urgent data, icon badge + subtitle
per section).

- [ ] **Step 2: `BranchShipments`**

Create `src/web/src/features/branches/components/BranchShipments.tsx`:

```typescript
import { Package } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import type {
  BranchShipmentCounts,
  BranchManifestCounts,
  DeliverySuccessStats,
} from "@/features/reports/types";

function formatRate(successes: number, attempts: number): string {
  return attempts === 0 ? "—" : `${((successes / attempts) * 100).toFixed(1)}%`;
}

export function BranchShipments({
  data,
}: {
  data: {
    shipmentCounts: BranchShipmentCounts;
    manifestCounts: BranchManifestCounts;
    todaysDeliveries: DeliverySuccessStats;
  };
}) {
  const tiles = [
    { label: "Outgoing", value: data.shipmentCounts.outgoing },
    { label: "Pending Sorting", value: data.shipmentCounts.pendingSorting },
    { label: "Pending Dispatch", value: data.shipmentCounts.pendingDispatch },
    { label: "Manifests In Transit", value: data.manifestCounts.inTransitTowardBranch },
    { label: "Manifests Recently Closed", value: data.manifestCounts.recentlyClosed },
    {
      label: "Today's Delivery Success",
      value: formatRate(data.todaysDeliveries.successes, data.todaysDeliveries.attempts),
    },
  ];
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Package className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Shipments &amp; Manifests
        </CardTitle>
        <CardDescription>Operational activity for this branch</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-3">
        {tiles.map((tile) => (
          <div key={tile.label} className="flex flex-col gap-1">
            <span className="text-xs text-muted-foreground">{tile.label}</span>
            <span className="font-sans text-2xl font-semibold text-foreground">{tile.value}</span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 3: Write the failing test for `BranchExceptions`**

Create `src/web/src/features/branches/components/BranchExceptions.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { BranchExceptions } from "./BranchExceptions";

describe("BranchExceptions", () => {
  it("aggregates the riders array into a count and total open tasks, not a named list", () => {
    render(
      <BranchExceptions
        data={{
          missingShipmentExceptions: 2,
          openNdrCount: 5,
          riders: [
            { riderId: "r1", openTaskCount: 3 },
            { riderId: "r2", openTaskCount: 4 },
          ],
          codPendingFinanceVerification: 1500,
        }}
      />
    );

    expect(screen.getByText("2 riders")).toBeInTheDocument();
    expect(screen.getByText("7 open tasks")).toBeInTheDocument();
    expect(screen.queryByText("r1")).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `cd src/web && npm test -- BranchExceptions.test.tsx`
Expected: FAIL — `./BranchExceptions` does not exist yet.

- [ ] **Step 5: Implement `BranchExceptions`**

Create `src/web/src/features/branches/components/BranchExceptions.tsx`:

```typescript
import { AlertTriangle } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

export function BranchExceptions({
  data,
}: {
  data: {
    missingShipmentExceptions: number;
    openNdrCount: number;
    riders: Array<{ riderId: string; openTaskCount: number }>;
    codPendingFinanceVerification: number;
  };
}) {
  const totalOpenTasks = data.riders.reduce((sum, r) => sum + r.openTaskCount, 0);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <AlertTriangle className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Exceptions
        </CardTitle>
        <CardDescription>Needs attention at this branch</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-4">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Missing Shipments</span>
          <span className="font-sans text-2xl font-semibold text-foreground">
            {data.missingShipmentExceptions}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Open NDRs</span>
          <span className="font-sans text-2xl font-semibold text-status-ndr">{data.openNdrCount}</span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">Riders</span>
          <span className="font-sans text-2xl font-semibold text-foreground">
            {data.riders.length} riders
          </span>
          <span className="text-xs text-muted-foreground">{totalOpenTasks} open tasks</span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">COD Pending Verification</span>
          <span className="font-sans text-2xl font-semibold text-foreground">
            {currencyFormatter.format(data.codPendingFinanceVerification)}
          </span>
        </div>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd src/web && npm test -- BranchExceptions.test.tsx`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/branches/components
git commit -m "feat(web): add branch dashboard content components"
```

---

### Task 10: Wire the dashboard section into branch detail; `BranchSwitcher` navigation; final verification

**Files:**
- Modify: `src/web/src/app/(staff)/branches/[id]/page.tsx`
- Modify: `src/web/src/components/layout/BranchSwitcher.tsx`
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: everything from Tasks 1-9.
- Produces: the complete Phase F2 feature — this task's exit criteria is the phase's exit
  criteria.

- [ ] **Step 1: Add the dashboard section to branch detail**

Modify `src/web/src/app/(staff)/branches/[id]/page.tsx`:

```typescript
"use client";

import { useParams } from "next/navigation";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranch } from "@/features/branches/hooks/useBranch";
import { useBranchDashboard } from "@/features/reports/hooks/useBranchDashboard";
import { BranchRecord } from "@/features/branches/BranchRecord";
import { BranchShipments } from "@/features/branches/components/BranchShipments";
import { BranchExceptions } from "@/features/branches/components/BranchExceptions";

export default function BranchDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data: branch, isLoading: isBranchLoading, isError: isBranchError, refetch: refetchBranch } = useBranch(id);
  const {
    data: dashboard,
    isLoading: isDashboardLoading,
    isError: isDashboardError,
    refetch: refetchDashboard,
  } = useBranchDashboard(id);

  if (isBranchLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isBranchError || !branch) {
    return (
      <ErrorState
        title="Couldn't load this branch"
        description="It may not exist, or you may not have access to it."
        onRetry={() => refetchBranch()}
      />
    );
  }

  return (
    <div className="space-y-6">
      <BranchRecord branch={branch} />
      {isDashboardLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {isDashboardError && (
        <ErrorState
          title="Couldn't load this branch's dashboard"
          description="Something went wrong fetching today's activity."
          onRetry={() => refetchDashboard()}
        />
      )}
      {dashboard && (
        <>
          <BranchShipments
            data={{
              shipmentCounts: dashboard.shipmentCounts,
              manifestCounts: dashboard.manifestCounts,
              todaysDeliveries: dashboard.todaysDeliveries,
            }}
          />
          <BranchExceptions
            data={{
              missingShipmentExceptions: dashboard.missingShipmentExceptions,
              openNdrCount: dashboard.openNdrCount,
              riders: dashboard.riders,
              codPendingFinanceVerification: dashboard.codPendingFinanceVerification,
            }}
          />
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Wire `BranchSwitcher` to navigate on selection**

Modify `src/web/src/components/layout/BranchSwitcher.tsx`: add `useRouter` and call
`router.push(\`/branches/${branch.branchId}\`)` in the `onSelect` handler alongside the existing
`setActiveBranchId` call:

```typescript
"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { Building2, ChevronDown } from "lucide-react";
import { useSession } from "@/features/auth/hooks/useSession";
import { useUiStore } from "@/lib/store/uiStore";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Button } from "@/components/ui/button";

export function BranchSwitcher() {
  const router = useRouter();
  const { data: session } = useSession();
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const setActiveBranchId = useUiStore((s) => s.setActiveBranchId);

  useEffect(() => {
    if (session && session.branches.length > 0 && !activeBranchId) {
      setActiveBranchId(session.branches[0].branchId);
    }
  }, [session, activeBranchId, setActiveBranchId]);

  if (!session || session.branches.length < 2) {
    return null;
  }

  const active = session.branches.find((b) => b.branchId === activeBranchId);

  function handleSelect(branchId: string) {
    setActiveBranchId(branchId);
    router.push(`/branches/${branchId}`);
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="gap-1.5">
          <Building2 className="h-3.5 w-3.5" strokeWidth={2} />
          {active?.branchName ?? "Select branch"}
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" strokeWidth={2} />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent>
        {session.branches.map((branch) => (
          <DropdownMenuItem key={branch.branchId} onSelect={() => handleSelect(branch.branchId)}>
            {branch.branchName}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

- [ ] **Step 3: Run the full frontend test suite**

Run: `cd src/web && npm test`
Expected: PASS — every test added across Tasks 1-10.

- [ ] **Step 4: Run the frontend build**

Run: `cd src/web && npm run build`
Expected: build succeeds with no type errors.

- [ ] **Step 5: Manual end-to-end verification**

Start Postgres/API (`docker compose up -d api`, reusing the shared containers) and the frontend dev
server. In a browser, as a registered org owner (`hasAllBranchesScope: true`, so `branch.create`/
`branch.update` are held everywhere):

1. Navigate to `/branches` from the sidebar. Confirm the list renders (possibly empty on a fresh
   org) and "New Branch" is visible.
2. Create a branch: fill the form, confirm the geography selects cascade correctly (District
   disabled until Province chosen, Local Level disabled until District chosen), submit, confirm
   redirect to the new branch's `/branches/{id}`.
3. On the detail page, confirm every field renders read-only, and the Delivery Zone select is
   editable (create a delivery zone via `POST /api/v1/delivery-zones` directly against the API
   first if none exist, so the select has an option to pick) — pick one, confirm it persists on
   reload.
4. Confirm the branch dashboard sections render below the record (likely all zeros/dashes for a
   fresh branch).
5. If the test org has 2+ `UserBranch` grants (grant one via the API if needed), confirm
   `BranchSwitcher` appears in `TopNav` and clicking a branch navigates to that branch's detail
   page. This is the concrete proof of `docs/FRONTEND-ROADMAP.md`'s Phase F2 exit criteria
   ("a user with multi-branch access can switch branches and see branch-scoped data change
   accordingly"). If the test org only has one branch, confirm `BranchSwitcher` correctly renders
   nothing (F0's existing "renders nothing for <2 branches" behavior) rather than fabricating a
   pass — note in the roadmap status which case was actually exercised.

- [ ] **Step 6: Update the roadmap status**

In `docs/FRONTEND-ROADMAP.md`, replace Phase F2's `*Status: Not started.*` line with a completion
note in the same style as Phases F0/F1's, written only after Step 5's manual verification actually
passes.

- [ ] **Step 7: Commit**

```bash
git add "src/web/src/app/(staff)/branches/[id]/page.tsx" src/web/src/components/layout/BranchSwitcher.tsx docs/FRONTEND-ROADMAP.md
git commit -m "feat(web): wire branch dashboard into detail page and BranchSwitcher navigation; close out Phase F2"
```
