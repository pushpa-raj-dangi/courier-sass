# Frontend Phase F3 (Shipments) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Shipments feature area — a shipment list, a Sheet-based booking (create) flow
with a live price-quote preview, and a Sheet-based detail view — matching the real backend
contract exactly (no tracking timeline, no list filters — both real gaps, deferred per the spec).

**Architecture:** Mirrors Phase F2's Branches feature exactly: BFF route handlers proxy the real
API, TanStack Query hooks call them, a feature-scoped Zustand store drives two globally-mounted
Sheets (create/detail) per `docs/ARCHITECTURE.md` §9a — no separate routed pages for create/detail.

**Tech Stack:** Next.js 14 App Router, TypeScript, TanStack Query 5, React Hook Form + Zod v4,
Zustand, shadcn/ui (`Sheet`, `Select`, `Input`, `Label`, `Button`, `Card`), the existing
`DataTable`, `StatusBadge`, and `GeographyPicker` components (reused verbatim, not rebuilt).

**Spec:** `docs/superpowers/specs/2026-08-29-frontend-phase-f3-shipments-design.md`

## Global Constraints

- Detail and create UI is a right-side `Sheet` driven by a Zustand store, never a route or
  `next/link` — `docs/ARCHITECTURE.md` §9a, no exceptions for form complexity.
- The frontend never computes or edits price/COD/declared-value — every financial figure shown is
  an echo of what the server returned (CLAUDE.md §33). The live quote preview is clearly a
  recalculating estimate, never treated as authoritative.
- Zod v4 syntax: `{ error: "..." }`, not v3's `required_error`/`invalid_type_error`.
- BFF routes are thin proxies: `apiFetch` server-side, `NextResponse.json(body, { status:
  response.status })` on success, `NextResponse.json(null, { status: 401 })` on a caught
  `NOT_AUTHENTICATED` error — matching every existing route in `app/api/*` exactly.
- `ServiceTypeResponse.Code` serializes as a raw integer (no `JsonStringEnumConverter` — same
  latent bug class `BranchType` had before its Phase F2 fix). Never render `.code` directly;
  only ever consume `.id`/`.name`.
- Run `cd src/web && npm test` and `npm run build` after every task-affecting change compiles
  cleanly; run the full suite again in Task 10.

---

### Task 1: `CLARIFICATIONS.md` entry for the two scope decisions

**Files:**
- Modify: `docs/CLARIFICATIONS.md`

**Interfaces:** None (pure documentation).

- [ ] **Step 1: Add entry §14**

Find the end of the existing numbered entries (the last is `## 13. Phase 12: ...`, followed by a
`## Summary of schema additions implied by this document` section). Insert a new `## 14.` entry
immediately before that summary section:

```markdown
## 14. Frontend Phase F3: no staff-facing tracking-event endpoint, no shipment list filters

**Gap 1 — tracking timeline.** `ShipmentResponse` exposes only `CurrentStatus`; no endpoint
projects a shipment's `ShipmentEvent` history for staff use. The only reader of `ShipmentEvent`
rows is `PublicTrackingService` (backing the public `/track/{awb}` endpoint), which is
deliberately limited per CLAUDE.md §24 — it excludes internal branch/user/notes data, so it's the
wrong shape for an internal ops view even if reused as-is.

**Resolution:** Frontend Phase F3 ships the shipment detail Sheet without a tracking timeline —
current status only, via `StatusBadge`. **Unblocks with:** a new staff-facing
`GET /api/v1/shipments/{awb}/events` endpoint (or equivalent) projecting `ShipmentEvent` rows
(`EventType, PreviousStatus, NewStatus, BranchId, UserId, RiderId, Timestamp, Notes`) for the
caller's own org, permission `shipment.read`. Not built this phase — backend work is paused.

**Gap 2 — list filters.** `GET /api/v1/shipments` accepts only `page`/`pageSize` — no status,
branch, date-range, or AWB/recipient search query parameter.

**Resolution:** Frontend Phase F3 ships the shipment list with server-side pagination only, no
filter UI. **Unblocks with:** query parameters on the existing list endpoint (e.g. `status`,
`originBranchId`/`destinationBranchId`, `createdFrom`/`createdTo`, `search`) plus the
corresponding `ShipmentService.ListAsync` filtering logic. Not built this phase.

Both decisions made with the user during Phase F3's brainstorming (2026-08-29), consistent with
the "for now focus on frontend" instruction governing this and neighboring phases.
```

- [ ] **Step 2: Commit**

```bash
git add docs/CLARIFICATIONS.md
git commit -m "docs: record Frontend Phase F3's two backend-gap scope decisions"
```

---

### Task 2: Shipment types, BFF routes, and hooks (list/detail/create)

**Files:**
- Create: `src/web/src/features/shipments/types.ts`
- Create: `src/web/src/app/api/shipments/route.ts`
- Create: `src/web/src/app/api/shipments/route.test.ts`
- Create: `src/web/src/app/api/shipments/[awb]/route.ts`
- Create: `src/web/src/app/api/shipments/[awb]/route.test.ts`
- Create: `src/web/src/features/shipments/hooks/useShipments.ts`
- Create: `src/web/src/features/shipments/hooks/useShipment.ts`
- Create: `src/web/src/features/shipments/hooks/useCreateShipment.ts`

**Interfaces:**
- Produces: `Shipment`, `ShipmentItem`, `CreateShipmentInput`, `CreateShipmentItemInput` types;
  `useShipments(page, pageSize): UseQueryResult<PagedResult<Shipment>>`,
  `useShipment(awb: string): UseQueryResult<Shipment>` (guarded `enabled: awb !== ""`),
  `useCreateShipment(): UseMutationResult<Shipment, unknown, CreateShipmentInput>`.

- [ ] **Step 1: Write `types.ts`**

Create `src/web/src/features/shipments/types.ts`:

```typescript
export interface ShipmentItem {
  id: string;
  description: string;
  quantity: number;
  unitValue: number;
}

export interface Shipment {
  id: string;
  awb: string;
  merchantId: string;
  originBranchId: string;
  destinationBranchId: string;
  currentBranchId: string | null;
  currentCustodianUserId: string | null;
  currentCustodianRiderId: string | null;
  currentStatus: string;
  serviceTypeId: string;
  weightKg: number;
  lengthCm: number | null;
  widthCm: number | null;
  heightCm: number | null;
  declaredValue: number;
  codAmount: number;
  wantsInsurance: boolean;
  deliveryCharge: number;
  insuranceCharge: number;
  otherCharges: number;
  totalCharge: number;
  recipientName: string;
  recipientPhone: string;
  recipientAddressLine: string;
  senderName: string;
  senderPhone: string;
  senderAddressLine: string;
  items: ShipmentItem[];
  createdAt: string;
}

export interface CreateShipmentItemInput {
  description: string;
  quantity: number;
  unitValue: number;
}

export interface CreateShipmentInput {
  merchantId: string;
  originBranchId: string;
  destinationBranchId: string;
  serviceTypeId: string;
  weightKg: number;
  lengthCm?: number;
  widthCm?: number;
  heightCm?: number;
  codAmount: number;
  wantsInsurance: boolean;
  recipientName: string;
  recipientPhone: string;
  recipientProvinceId: string;
  recipientDistrictId: string;
  recipientLocalLevelId: string;
  recipientWardNumber: number;
  recipientAddressLine: string;
  senderName: string;
  senderPhone: string;
  senderProvinceId: string;
  senderDistrictId: string;
  senderLocalLevelId: string;
  senderWardNumber: number;
  senderAddressLine: string;
  items: CreateShipmentItemInput[];
}
```

- [ ] **Step 2: Write the failing test for the list/create BFF route**

Create `src/web/src/app/api/shipments/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments", () => {
  it("passes page/pageSize through to the backend and proxies the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new Request("http://localhost/api/shipments?page=2&pageSize=10");
    const response = await GET(request as never);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments?page=2&pageSize=10");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new Request("http://localhost/api/shipments");
    const response = await GET(request as never);

    expect(response.status).toBe(401);
  });
});

describe("POST /api/shipments", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "s1", awb: "AWB123" }),
    } as Response);

    const request = new Request("http://localhost/api/shipments", {
      method: "POST",
      body: JSON.stringify({ weightKg: 2 }),
    });
    const response = await POST(request as never);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/shipments",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ weightKg: 2 }) })
    );
    expect(response.status).toBe(201);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- shipments/route.test.ts`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 4: Implement the list/create BFF route**

Create `src/web/src/app/api/shipments/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/shipments${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/shipments", {
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

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- shipments/route.test.ts`
Expected: PASS.

- [ ] **Step 6: Write the failing test for the detail BFF route**

Create `src/web/src/app/api/shipments/[awb]/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/[awb]", () => {
  it("proxies the backend response for the given awb", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ awb: "AWB123" }),
    } as Response);

    const response = await GET({} as Request, { params: { awb: "AWB123" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/AWB123");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET({} as Request, { params: { awb: "AWB123" } });

    expect(response.status).toBe(401);
  });
});
```

- [ ] **Step 7: Run the test to verify it fails**

Run: `cd src/web && npm test -- "shipments/\[awb\]/route.test.ts"`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 8: Implement the detail BFF route**

Create `src/web/src/app/api/shipments/[awb]/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(_request: Request, { params }: { params: { awb: string } }) {
  try {
    const response = await apiFetch(`/api/v1/shipments/${params.awb}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 9: Run the test to verify it passes**

Run: `cd src/web && npm test -- "shipments/\[awb\]/route.test.ts"`
Expected: PASS.

- [ ] **Step 10: Write the hooks (no dedicated test — bare TanStack Query wrappers, matching
  `useBranches`/`useBranch`'s established precedent of no test file for pure passthrough hooks)**

Create `src/web/src/features/shipments/hooks/useShipments.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Shipment } from "@/features/shipments/types";

export function useShipments(page: number, pageSize: number) {
  return useQuery({
    queryKey: ["shipments", page, pageSize],
    queryFn: () =>
      clientFetch<PagedResult<Shipment>>(`/api/shipments?page=${page}&pageSize=${pageSize}`),
  });
}
```

Create `src/web/src/features/shipments/hooks/useShipment.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment } from "@/features/shipments/types";

export function useShipment(awb: string) {
  return useQuery({
    queryKey: ["shipments", awb],
    queryFn: () => clientFetch<Shipment>(`/api/shipments/${awb}`),
    enabled: awb !== "",
  });
}
```

Create `src/web/src/features/shipments/hooks/useCreateShipment.ts`:

```typescript
"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Shipment, CreateShipmentInput } from "@/features/shipments/types";

export function useCreateShipment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateShipmentInput) =>
      clientFetch<Shipment>("/api/shipments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["shipments"] });
    },
  });
}
```

- [ ] **Step 11: Run the full test suite**

Run: `cd src/web && npm test`
Expected: PASS — every existing test plus this task's new ones.

- [ ] **Step 12: Commit**

```bash
git add src/web/src/features/shipments/types.ts src/web/src/app/api/shipments src/web/src/features/shipments/hooks
git commit -m "feat(web): add shipment types, BFF routes, and list/detail/create hooks"
```

---

### Task 3: Reference data — service types and merchants

**Files:**
- Modify: `src/web/src/features/shipments/types.ts`
- Create: `src/web/src/app/api/pricing/service-types/route.ts`
- Create: `src/web/src/app/api/merchants/route.ts`
- Create: `src/web/src/features/shipments/hooks/useServiceTypes.ts`
- Create: `src/web/src/features/shipments/hooks/useMerchants.ts`

**Interfaces:**
- Consumes: nothing from Task 2 directly (independent reference data).
- Produces: `ServiceType`, `Merchant` types; `useServiceTypes(): UseQueryResult<ServiceType[]>`,
  `useMerchants(): UseQueryResult<Merchant[]>`. Task 8's create Sheet consumes both.

- [ ] **Step 1: Extend `types.ts`**

Append to `src/web/src/features/shipments/types.ts`:

```typescript
export interface ServiceType {
  id: string;
  // `code` deliberately omitted — ServiceTypeResponse.Code has no JsonStringEnumConverter and
  // serializes as a raw integer over the wire (same latent bug BranchType had before its
  // Phase F2 fix). Only `id`/`name` are safe to consume; never render `.code`.
  name: string;
}

export interface Merchant {
  id: string;
  displayName: string;
  isActive: boolean;
}
```

- [ ] **Step 2: BFF routes (no dedicated tests — thin GET-only passthroughs, matching
  `app/api/delivery-zones/route.ts`'s established precedent of no test file for this shape)**

Create `src/web/src/app/api/pricing/service-types/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/pricing/reference/service-types");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

Create `src/web/src/app/api/merchants/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/merchants");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 3: Hooks**

Create `src/web/src/features/shipments/hooks/useServiceTypes.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { ServiceType } from "@/features/shipments/types";

export function useServiceTypes() {
  return useQuery({
    queryKey: ["service-types"],
    queryFn: () => clientFetch<ServiceType[]>("/api/pricing/service-types"),
  });
}
```

Create `src/web/src/features/shipments/hooks/useMerchants.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Merchant } from "@/features/shipments/types";

export function useMerchants() {
  return useQuery({
    queryKey: ["merchants"],
    queryFn: () => clientFetch<Merchant[]>("/api/merchants"),
  });
}
```

- [ ] **Step 4: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/shipments/types.ts src/web/src/app/api/pricing src/web/src/app/api/merchants src/web/src/features/shipments/hooks
git commit -m "feat(web): add service-type and merchant reference data for shipment booking"
```

---

### Task 4: Price quote BFF route and debounced hook

**Files:**
- Modify: `src/web/src/features/shipments/types.ts`
- Create: `src/web/src/app/api/pricing/quote/route.ts`
- Create: `src/web/src/features/shipments/hooks/useShipmentQuote.ts`
- Test: `src/web/src/features/shipments/hooks/useShipmentQuote.test.tsx`

**Interfaces:**
- Consumes: nothing new.
- Produces: `QuoteInput`, `QuoteResponse` types;
  `useShipmentQuote(input: QuoteInput | null): UseQueryResult<QuoteResponse>` — debounces its own
  input by 500ms internally and is `enabled` only once every required field is present. Task 8
  consumes this directly.

- [ ] **Step 1: Extend `types.ts`**

Append to `src/web/src/features/shipments/types.ts`:

```typescript
export interface QuoteInput {
  originBranchId: string;
  destinationBranchId: string;
  weightKg: number;
  serviceTypeId: string;
  codAmount: number;
  declaredValue: number;
  wantsInsurance: boolean;
  merchantId: string | null;
}

export interface AppliedCharge {
  type: string;
  amount: number;
}

export interface QuoteResponse {
  baseRate: number;
  appliedCharges: AppliedCharge[];
  discountPercent: number;
  subtotal: number;
  total: number;
}
```

- [ ] **Step 2: BFF route (no dedicated test — thin POST passthrough, matching
  `app/api/branches/[id]/delivery-zone/route.ts`'s established precedent)**

Create `src/web/src/app/api/pricing/quote/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/pricing/quote", {
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

- [ ] **Step 3: Write the failing test for the debounced hook**

Create `src/web/src/features/shipments/hooks/useShipmentQuote.test.tsx`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useShipmentQuote } from "./useShipmentQuote";
import * as clientFetchModule from "@/lib/api/clientFetch";

function wrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useShipmentQuote", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  it("does not fetch when input is null", () => {
    const spy = vi.spyOn(clientFetchModule, "clientFetch");
    renderHook(() => useShipmentQuote(null), { wrapper });
    expect(spy).not.toHaveBeenCalled();
  });

  it("fetches a quote after the debounce delay once given a full input", async () => {
    const spy = vi
      .spyOn(clientFetchModule, "clientFetch")
      .mockResolvedValue({ baseRate: 100, appliedCharges: [], discountPercent: 0, subtotal: 100, total: 100 });

    const input = {
      originBranchId: "b1",
      destinationBranchId: "b2",
      weightKg: 2,
      serviceTypeId: "st1",
      codAmount: 0,
      declaredValue: 500,
      wantsInsurance: false,
      merchantId: "m1",
    };
    renderHook(() => useShipmentQuote(input), { wrapper });

    expect(spy).not.toHaveBeenCalled();
    vi.advanceTimersByTime(500);
    await waitFor(() => expect(spy).toHaveBeenCalledWith(
      "/api/pricing/quote",
      expect.objectContaining({ method: "POST" })
    ));
  });
});
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `cd src/web && npm test -- useShipmentQuote.test.tsx`
Expected: FAIL — `./useShipmentQuote` does not exist yet.

- [ ] **Step 5: Implement the debounced hook**

Create `src/web/src/features/shipments/hooks/useShipmentQuote.ts`:

```typescript
"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { QuoteInput, QuoteResponse } from "@/features/shipments/types";

export function useShipmentQuote(input: QuoteInput | null) {
  const [debounced, setDebounced] = useState<QuoteInput | null>(null);

  useEffect(() => {
    const timeout = setTimeout(() => setDebounced(input), 500);
    return () => clearTimeout(timeout);
  }, [input]);

  return useQuery({
    queryKey: ["shipment-quote", debounced],
    queryFn: () =>
      clientFetch<QuoteResponse>("/api/pricing/quote", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(debounced),
      }),
    enabled: debounced !== null,
  });
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd src/web && npm test -- useShipmentQuote.test.tsx`
Expected: PASS.

- [ ] **Step 7: Run the full test suite**

Run: `cd src/web && npm test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/shipments/types.ts src/web/src/app/api/pricing/quote src/web/src/features/shipments/hooks/useShipmentQuote.ts src/web/src/features/shipments/hooks/useShipmentQuote.test.tsx
git commit -m "feat(web): add price-quote BFF route and debounced quote hook"
```

---

### Task 5: `shipmentPanelStore`

**Files:**
- Create: `src/web/src/features/shipments/store/shipmentPanelStore.ts`
- Test: `src/web/src/features/shipments/store/shipmentPanelStore.test.ts`

**Interfaces:**
- Produces: `useShipmentPanelStore` — `{ panel: { mode: "closed" } | { mode: "create" } |
  { mode: "detail"; awb: string }, openCreate(), openDetail(awb), close() }`. Tasks 7, 8, 9
  consume this.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/shipments/store/shipmentPanelStore.test.ts`:

```typescript
import { describe, expect, it, beforeEach } from "vitest";
import { useShipmentPanelStore } from "./shipmentPanelStore";

describe("useShipmentPanelStore", () => {
  beforeEach(() => {
    useShipmentPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate switches to the create panel", () => {
    useShipmentPanelStore.getState().openCreate();
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openDetail switches to the detail panel with the given awb", () => {
    useShipmentPanelStore.getState().openDetail("AWB123");
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "detail", awb: "AWB123" });
  });

  it("close returns to closed from either panel", () => {
    useShipmentPanelStore.getState().openDetail("AWB123");
    useShipmentPanelStore.getState().close();
    expect(useShipmentPanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- shipmentPanelStore.test.ts`
Expected: FAIL — `./shipmentPanelStore` does not exist yet.

- [ ] **Step 3: Implement the store**

Create `src/web/src/features/shipments/store/shipmentPanelStore.ts`:

```typescript
import { create } from "zustand";

type ShipmentPanelState =
  | { mode: "closed" }
  | { mode: "create" }
  | { mode: "detail"; awb: string };

interface ShipmentPanelStore {
  panel: ShipmentPanelState;
  openCreate: () => void;
  openDetail: (awb: string) => void;
  close: () => void;
}

// Global so any component in the (staff) tree can open the shipment sheet without routing to a
// separate page (docs/ARCHITECTURE.md §9a). Keyed by AWB, not a Guid id — GET /{awb} is how the
// backend reads a shipment, and it's the natural human-facing identifier for this domain.
export const useShipmentPanelStore = create<ShipmentPanelStore>((set) => ({
  panel: { mode: "closed" },
  openCreate: () => set({ panel: { mode: "create" } }),
  openDetail: (awb) => set({ panel: { mode: "detail", awb } }),
  close: () => set({ panel: { mode: "closed" } }),
}));
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- shipmentPanelStore.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/shipments/store
git commit -m "feat(web): add shipmentPanelStore for the Sheet-based create/detail UI"
```

---

### Task 6: Create-form schema and the Items repeater

**Files:**
- Create: `src/web/src/features/shipments/schema.ts`
- Create: `src/web/src/features/shipments/components/ItemsField.tsx`
- Test: `src/web/src/features/shipments/components/ItemsField.test.tsx`

**Interfaces:**
- Produces: `createShipmentSchema`, `CreateShipmentFormValues` (RHF/Zod); `<ItemsField control
  errors />` — a `useFieldArray`-backed repeater for the `items` array. Task 8 consumes both.

- [ ] **Step 1: Write the schema**

Create `src/web/src/features/shipments/schema.ts`:

```typescript
import { z } from "zod";

const itemSchema = z.object({
  description: z.string().min(1, "Description is required."),
  quantity: z.number({ error: "Quantity is required." }).positive("Quantity must be positive."),
  unitValue: z.number({ error: "Unit value is required." }).nonnegative("Unit value cannot be negative."),
});

export const createShipmentSchema = z.object({
  merchantId: z.string({ error: "Merchant is required." }).min(1, "Merchant is required."),
  originBranchId: z.string({ error: "Origin branch is required." }).min(1, "Origin branch is required."),
  destinationBranchId: z.string({ error: "Destination branch is required." }).min(1, "Destination branch is required."),
  serviceTypeId: z.string({ error: "Service type is required." }).min(1, "Service type is required."),
  weightKg: z.number({ error: "Weight is required." }).positive("Weight must be positive."),
  lengthCm: z.number().positive().optional(),
  widthCm: z.number().positive().optional(),
  heightCm: z.number().positive().optional(),
  codAmount: z.number({ error: "COD amount is required." }).nonnegative("COD amount cannot be negative."),
  wantsInsurance: z.boolean(),
  recipientName: z.string().min(1, "Recipient name is required."),
  recipientPhone: z.string().min(1, "Recipient phone is required."),
  recipientProvinceId: z.string({ error: "Recipient province is required." }).min(1, "Recipient province is required."),
  recipientDistrictId: z.string({ error: "Recipient district is required." }).min(1, "Recipient district is required."),
  recipientLocalLevelId: z.string({ error: "Recipient local level is required." }).min(1, "Recipient local level is required."),
  recipientWardNumber: z.number({ error: "Recipient ward number is required." }).positive(),
  recipientAddressLine: z.string().min(1, "Recipient address is required."),
  senderName: z.string().min(1, "Sender name is required."),
  senderPhone: z.string().min(1, "Sender phone is required."),
  senderProvinceId: z.string({ error: "Sender province is required." }).min(1, "Sender province is required."),
  senderDistrictId: z.string({ error: "Sender district is required." }).min(1, "Sender district is required."),
  senderLocalLevelId: z.string({ error: "Sender local level is required." }).min(1, "Sender local level is required."),
  senderWardNumber: z.number({ error: "Sender ward number is required." }).positive(),
  senderAddressLine: z.string().min(1, "Sender address is required."),
  // At least one item — DeclaredValue is derived server-side from this array (sum of
  // quantity * unitValue), so an empty array would price/declare the shipment as worthless.
  items: z.array(itemSchema).min(1, "Add at least one item."),
});

export type CreateShipmentFormValues = z.infer<typeof createShipmentSchema>;
```

- [ ] **Step 2: Write the failing test for `ItemsField`**

Create `src/web/src/features/shipments/components/ItemsField.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useForm, FormProvider } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { createShipmentSchema, type CreateShipmentFormValues } from "@/features/shipments/schema";
import { ItemsField } from "./ItemsField";

function Harness() {
  const methods = useForm<CreateShipmentFormValues>({
    resolver: zodResolver(createShipmentSchema),
    defaultValues: { items: [{ description: "", quantity: 1, unitValue: 0 }] },
  });
  return (
    <FormProvider {...methods}>
      <ItemsField />
    </FormProvider>
  );
}

describe("ItemsField", () => {
  it("starts with one row and adds another on 'Add Item'", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    expect(screen.getAllByLabelText(/description/i)).toHaveLength(1);

    await user.click(screen.getByRole("button", { name: /add item/i }));

    expect(screen.getAllByLabelText(/description/i)).toHaveLength(2);
  });

  it("removes a row when its remove button is clicked", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: /add item/i }));
    expect(screen.getAllByLabelText(/description/i)).toHaveLength(2);

    await user.click(screen.getAllByRole("button", { name: /remove item/i })[0]);
    expect(screen.getAllByLabelText(/description/i)).toHaveLength(1);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- ItemsField.test.tsx`
Expected: FAIL — `./ItemsField` does not exist yet.

- [ ] **Step 4: Implement `ItemsField`**

Create `src/web/src/features/shipments/components/ItemsField.tsx`:

```typescript
"use client";

import { useFieldArray, useFormContext } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { CreateShipmentFormValues } from "@/features/shipments/schema";

export function ItemsField() {
  const { control, register, formState } = useFormContext<CreateShipmentFormValues>();
  const { fields, append, remove } = useFieldArray({ control, name: "items" });

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <Label>Items</Label>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="gap-1.5"
          onClick={() => append({ description: "", quantity: 1, unitValue: 0 })}
        >
          <Plus className="h-3.5 w-3.5" strokeWidth={2} />
          Add Item
        </Button>
      </div>
      {fields.map((field, index) => (
        <div key={field.id} className="grid grid-cols-[1fr_auto_auto_auto] items-end gap-2">
          <div className="space-y-1">
            <Label htmlFor={`items.${index}.description`}>Description</Label>
            <Input id={`items.${index}.description`} {...register(`items.${index}.description`)} />
          </div>
          <div className="w-20 space-y-1">
            <Label htmlFor={`items.${index}.quantity`}>Qty</Label>
            <Input
              id={`items.${index}.quantity`}
              type="number"
              {...register(`items.${index}.quantity`, { valueAsNumber: true })}
            />
          </div>
          <div className="w-28 space-y-1">
            <Label htmlFor={`items.${index}.unitValue`}>Unit Value</Label>
            <Input
              id={`items.${index}.unitValue`}
              type="number"
              {...register(`items.${index}.unitValue`, { valueAsNumber: true })}
            />
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label="Remove item"
            onClick={() => remove(index)}
          >
            <Trash2 className="h-4 w-4" strokeWidth={2} />
          </Button>
        </div>
      ))}
      {formState.errors.items?.message && (
        <p className="text-sm text-destructive">{formState.errors.items.message}</p>
      )}
    </div>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- ItemsField.test.tsx`
Expected: PASS.

- [ ] **Step 6: Run the full test suite**

Run: `cd src/web && npm test`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/shipments/schema.ts src/web/src/features/shipments/components/ItemsField.tsx src/web/src/features/shipments/components/ItemsField.test.tsx
git commit -m "feat(web): add shipment create-form schema and Items repeater"
```

---

### Task 7: Shipment list page

**Files:**
- Create: `src/web/src/app/(staff)/shipments/page.tsx`

**Interfaces:**
- Consumes: `useShipments` (Task 2), `useShipmentPanelStore` (Task 5), `useServiceTypes` (Task 3,
  for the Service Type column label), `usePermission`, `DataTable`, `StatusBadge`.
- Produces: the `/shipments` route.

- [ ] **Step 1: Implement the list page**

Create `src/web/src/app/(staff)/shipments/page.tsx`:

```typescript
"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useShipments } from "@/features/shipments/hooks/useShipments";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import type { Shipment } from "@/features/shipments/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

export default function ShipmentsPage() {
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError, refetch } = useShipments(page, pageSize);
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
        title="Couldn't load shipments"
        description="Something went wrong fetching the shipment list."
        onRetry={() => refetch()}
      />
    );
  }

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
      <DataTable
        columns={columns}
        data={data}
        isLoading={false}
        emptyTitle="No shipments yet"
        emptyDescription="Book your first shipment to get started."
        onPageChange={setPage}
        rowKey={(s) => s.id}
      />
    </div>
  );
}
```

- [ ] **Step 2: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS — the new `/shipments` route appears in the build's route list.

- [ ] **Step 3: Commit**

```bash
git add "src/web/src/app/(staff)/shipments"
git commit -m "feat(web): add shipment list page"
```

---

### Task 8: `ShipmentCreateSheet`

**Files:**
- Create: `src/web/src/features/shipments/components/ShipmentCreateSheet.tsx`

**Interfaces:**
- Consumes: `useCreateShipment`, `useMerchants`, `useServiceTypes`, `useShipmentQuote`,
  `useShipmentPanelStore`, `GeographyPicker` (from `features/branches/GeographyPicker`, reused
  verbatim), `ItemsField`, `createShipmentSchema`, `useBranches` (from `features/branches/hooks`).
- Produces: `<ShipmentCreateSheet />`, mounted globally in Task 10.

- [ ] **Step 1: Implement the create Sheet**

Create `src/web/src/features/shipments/components/ShipmentCreateSheet.tsx`:

```typescript
"use client";

import { useState } from "react";
import { useForm, Controller, FormProvider } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { createShipmentSchema, type CreateShipmentFormValues } from "@/features/shipments/schema";
import { useCreateShipment } from "@/features/shipments/hooks/useCreateShipment";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentQuote } from "@/features/shipments/hooks/useShipmentQuote";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { ItemsField } from "@/features/shipments/components/ItemsField";

export function ShipmentCreateSheet() {
  const panel = useShipmentPanelStore((s) => s.panel);
  const close = useShipmentPanelStore((s) => s.close);
  const openDetail = useShipmentPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "create";

  const createShipment = useCreateShipment();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();
  const { data: branches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const methods = useForm<CreateShipmentFormValues>({
    resolver: zodResolver(createShipmentSchema),
    defaultValues: { items: [{ description: "", quantity: 1, unitValue: 0 }], wantsInsurance: false },
  });
  const { register, handleSubmit, control, watch, setValue, reset, formState: { errors, isSubmitting } } = methods;

  const recipientGeography: GeographyValue = {
    provinceId: watch("recipientProvinceId") ?? null,
    districtId: watch("recipientDistrictId") ?? null,
    localLevelId: watch("recipientLocalLevelId") ?? null,
    wardNumber: watch("recipientWardNumber") ?? null,
  };
  const senderGeography: GeographyValue = {
    provinceId: watch("senderProvinceId") ?? null,
    districtId: watch("senderDistrictId") ?? null,
    localLevelId: watch("senderLocalLevelId") ?? null,
    wardNumber: watch("senderWardNumber") ?? null,
  };

  const [originBranchId, destinationBranchId, weightKg, serviceTypeId, codAmount, wantsInsurance, merchantId, items] =
    watch(["originBranchId", "destinationBranchId", "weightKg", "serviceTypeId", "codAmount", "wantsInsurance", "merchantId", "items"]);

  const declaredValue = (items ?? []).reduce((sum, item) => sum + (item.quantity || 0) * (item.unitValue || 0), 0);

  const quoteInput =
    originBranchId && destinationBranchId && weightKg && serviceTypeId
      ? {
          originBranchId,
          destinationBranchId,
          weightKg,
          serviceTypeId,
          codAmount: codAmount ?? 0,
          declaredValue,
          wantsInsurance: wantsInsurance ?? false,
          merchantId: merchantId ?? null,
        }
      : null;
  const { data: quote, isFetching: isQuoting } = useShipmentQuote(quoteInput);

  const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

  async function onSubmit(values: CreateShipmentFormValues) {
    setServerError(null);
    try {
      const created = await createShipment.mutateAsync(values);
      reset();
      openDetail(created.awb);
    } catch {
      setServerError("Couldn't create the shipment. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>New Shipment</SheetTitle>
        </SheetHeader>
        <FormProvider {...methods}>
          <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-6" noValidate>
            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Route</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="merchantId">Merchant</Label>
                  <Controller
                    name="merchantId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger id="merchantId" aria-label="Merchant">
                          <SelectValue placeholder="Select merchant" />
                        </SelectTrigger>
                        <SelectContent>
                          {merchants?.map((m) => (
                            <SelectItem key={m.id} value={m.id}>{m.displayName}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.merchantId && <p className="text-sm text-destructive">{errors.merchantId.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="serviceTypeId">Service Type</Label>
                  <Controller
                    name="serviceTypeId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger id="serviceTypeId" aria-label="Service type">
                          <SelectValue placeholder="Select service type" />
                        </SelectTrigger>
                        <SelectContent>
                          {serviceTypes?.map((s) => (
                            <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.serviceTypeId && <p className="text-sm text-destructive">{errors.serviceTypeId.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="originBranchId">Origin Branch</Label>
                  <Controller
                    name="originBranchId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger id="originBranchId" aria-label="Origin branch">
                          <SelectValue placeholder="Select origin branch" />
                        </SelectTrigger>
                        <SelectContent>
                          {branches?.map((b) => (
                            <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.originBranchId && <p className="text-sm text-destructive">{errors.originBranchId.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="destinationBranchId">Destination Branch</Label>
                  <Controller
                    name="destinationBranchId"
                    control={control}
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger id="destinationBranchId" aria-label="Destination branch">
                          <SelectValue placeholder="Select destination branch" />
                        </SelectTrigger>
                        <SelectContent>
                          {branches?.map((b) => (
                            <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    )}
                  />
                  {errors.destinationBranchId && <p className="text-sm text-destructive">{errors.destinationBranchId.message}</p>}
                </div>
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Package</h3>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                <div className="space-y-2">
                  <Label htmlFor="weightKg">Weight (kg)</Label>
                  <Input id="weightKg" type="number" step="0.1" {...register("weightKg", { valueAsNumber: true })} />
                  {errors.weightKg && <p className="text-sm text-destructive">{errors.weightKg.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="lengthCm">Length (cm)</Label>
                  <Input id="lengthCm" type="number" {...register("lengthCm", { valueAsNumber: true })} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="widthCm">Width (cm)</Label>
                  <Input id="widthCm" type="number" {...register("widthCm", { valueAsNumber: true })} />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="heightCm">Height (cm)</Label>
                  <Input id="heightCm" type="number" {...register("heightCm", { valueAsNumber: true })} />
                </div>
              </div>
              <ItemsField />
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Money</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="codAmount">COD Amount</Label>
                  <Input id="codAmount" type="number" {...register("codAmount", { valueAsNumber: true })} />
                  {errors.codAmount && <p className="text-sm text-destructive">{errors.codAmount.message}</p>}
                </div>
                <div className="flex items-center gap-2 pt-6">
                  <Controller
                    name="wantsInsurance"
                    control={control}
                    render={({ field }) => (
                      <Checkbox id="wantsInsurance" checked={field.value} onCheckedChange={field.onChange} />
                    )}
                  />
                  <Label htmlFor="wantsInsurance">Wants Insurance</Label>
                </div>
              </div>

              {/* Read-only, server-computed estimate — never editable, per CLAUDE.md §33 */}
              <div className="rounded-lg border border-border bg-muted/50 p-4">
                <p className="mb-2 text-xs text-muted-foreground">Estimated Price (recalculates as you fill in the route and package)</p>
                {isQuoting && <Spinner className="h-4 w-4" />}
                {quote && !isQuoting && (
                  <p className="font-sans text-lg font-semibold text-foreground">
                    {currencyFormatter.format(quote.total)}
                  </p>
                )}
                {!quote && !isQuoting && <p className="text-sm text-muted-foreground">—</p>}
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Recipient</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="recipientName">Name</Label>
                  <Input id="recipientName" {...register("recipientName")} />
                  {errors.recipientName && <p className="text-sm text-destructive">{errors.recipientName.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="recipientPhone">Phone</Label>
                  <Input id="recipientPhone" {...register("recipientPhone")} />
                  {errors.recipientPhone && <p className="text-sm text-destructive">{errors.recipientPhone.message}</p>}
                </div>
              </div>
              <GeographyPicker
                value={recipientGeography}
                onChange={(next) => {
                  setValue("recipientProvinceId", next.provinceId ?? "");
                  setValue("recipientDistrictId", next.districtId ?? "");
                  setValue("recipientLocalLevelId", next.localLevelId ?? "");
                  setValue("recipientWardNumber", next.wardNumber ?? undefined as never);
                }}
              />
              <div className="space-y-2">
                <Label htmlFor="recipientAddressLine">Address</Label>
                <Input id="recipientAddressLine" {...register("recipientAddressLine")} />
                {errors.recipientAddressLine && <p className="text-sm text-destructive">{errors.recipientAddressLine.message}</p>}
              </div>
            </section>

            <section className="space-y-4">
              <h3 className="text-sm font-semibold text-foreground">Sender</h3>
              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="senderName">Name</Label>
                  <Input id="senderName" {...register("senderName")} />
                  {errors.senderName && <p className="text-sm text-destructive">{errors.senderName.message}</p>}
                </div>
                <div className="space-y-2">
                  <Label htmlFor="senderPhone">Phone</Label>
                  <Input id="senderPhone" {...register("senderPhone")} />
                  {errors.senderPhone && <p className="text-sm text-destructive">{errors.senderPhone.message}</p>}
                </div>
              </div>
              <GeographyPicker
                value={senderGeography}
                onChange={(next) => {
                  setValue("senderProvinceId", next.provinceId ?? "");
                  setValue("senderDistrictId", next.districtId ?? "");
                  setValue("senderLocalLevelId", next.localLevelId ?? "");
                  setValue("senderWardNumber", next.wardNumber ?? undefined as never);
                }}
              />
              <div className="space-y-2">
                <Label htmlFor="senderAddressLine">Address</Label>
                <Input id="senderAddressLine" {...register("senderAddressLine")} />
                {errors.senderAddressLine && <p className="text-sm text-destructive">{errors.senderAddressLine.message}</p>}
              </div>
            </section>

            {serverError && <p className="text-sm text-destructive">{serverError}</p>}
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Shipment"}
            </Button>
          </form>
        </FormProvider>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 2: Install `@radix-ui/react-checkbox` and add the hand-authored `Checkbox` primitive**

No `Checkbox` component exists yet in `components/ui/` and `@radix-ui/react-checkbox` is not yet a
dependency. Install it, then hand-author the primitive against it directly (matching this
project's established pattern for every other primitive the interactive `npx shadcn add` CLI
prompt can't get through non-interactively — `dialog.tsx`, `select.tsx`, `sheet.tsx`):

```bash
cd src/web && npm install @radix-ui/react-checkbox
```

Create `src/web/src/components/ui/checkbox.tsx`:

```typescript
"use client"

import * as React from "react"
import * as CheckboxPrimitive from "@radix-ui/react-checkbox"
import { Check } from "lucide-react"

import { cn } from "@/lib/utils"

const Checkbox = React.forwardRef<
  React.ElementRef<typeof CheckboxPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof CheckboxPrimitive.Root>
>(({ className, ...props }, ref) => (
  <CheckboxPrimitive.Root
    ref={ref}
    className={cn(
      "peer h-4 w-4 shrink-0 rounded-sm border border-primary shadow ring-offset-background focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 data-[state=checked]:bg-primary data-[state=checked]:text-primary-foreground",
      className
    )}
    {...props}
  >
    <CheckboxPrimitive.Indicator className={cn("flex items-center justify-center text-current")}>
      <Check className="h-3.5 w-3.5" strokeWidth={3} />
    </CheckboxPrimitive.Indicator>
  </CheckboxPrimitive.Root>
))
Checkbox.displayName = CheckboxPrimitive.Root.displayName

export { Checkbox }
```

- [ ] **Step 3: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS.

- [ ] **Step 4: Commit**

```bash
git add src/web/src/features/shipments/components/ShipmentCreateSheet.tsx src/web/src/components/ui/checkbox.tsx
git commit -m "feat(web): add shipment booking Sheet with live quote preview"
```

---

### Task 9: `ShipmentDetailSheet`

**Files:**
- Create: `src/web/src/features/shipments/components/ShipmentDetailSheet.tsx`

**Interfaces:**
- Consumes: `useShipment` (Task 2), `useShipmentPanelStore` (Task 5), `useMerchants`,
  `useServiceTypes` (Task 3), `useBranches` (Branches feature), `StatusBadge`.
- Produces: `<ShipmentDetailSheet />`, mounted globally in Task 10.

- [ ] **Step 1: Implement the detail Sheet**

Create `src/web/src/features/shipments/components/ShipmentDetailSheet.tsx`:

```typescript
"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useServiceTypes } from "@/features/shipments/hooks/useServiceTypes";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function ShipmentDetailSheet() {
  const panel = useShipmentPanelStore((s) => s.panel);
  const close = useShipmentPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const awb = panel.mode === "detail" ? panel.awb : "";

  const { data: shipment, isLoading, isError, refetch } = useShipment(awb);
  const { data: branches } = useBranches();
  const { data: merchants } = useMerchants();
  const { data: serviceTypes } = useServiceTypes();

  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "—";
  const merchantName = (id: string) => merchants?.find((m) => m.id === id)?.displayName ?? "—";
  const serviceTypeName = (id: string) => serviceTypes?.find((s) => s.id === id)?.name ?? "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{shipment?.awb ?? "Shipment"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this shipment"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {shipment && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={shipment.currentStatus} />
                </div>
                <Field label="Service Type" value={serviceTypeName(shipment.serviceTypeId)} />
                <Field label="Merchant" value={merchantName(shipment.merchantId)} />
                <Field label="Created At" value={new Date(shipment.createdAt).toLocaleString()} />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <Field label="Origin Branch" value={branchName(shipment.originBranchId)} />
                <Field label="Destination Branch" value={branchName(shipment.destinationBranchId)} />
                <Field label="Weight" value={`${shipment.weightKg} kg`} />
                <Field
                  label="Dimensions"
                  value={
                    shipment.lengthCm && shipment.widthCm && shipment.heightCm
                      ? `${shipment.lengthCm} x ${shipment.widthCm} x ${shipment.heightCm} cm`
                      : "—"
                  }
                />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <Field label="Recipient" value={`${shipment.recipientName} (${shipment.recipientPhone})`} />
                <Field label="Recipient Address" value={shipment.recipientAddressLine} />
                <Field label="Sender" value={`${shipment.senderName} (${shipment.senderPhone})`} />
                <Field label="Sender Address" value={shipment.senderAddressLine} />
              </div>

              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <Field label="Declared Value" value={currencyFormatter.format(shipment.declaredValue)} />
                <Field label="COD Amount" value={currencyFormatter.format(shipment.codAmount)} />
                <Field label="Delivery Charge" value={currencyFormatter.format(shipment.deliveryCharge)} />
                <Field label="Insurance Charge" value={currencyFormatter.format(shipment.insuranceCharge)} />
                <Field label="Other Charges" value={currencyFormatter.format(shipment.otherCharges)} />
                <Field label="Total Charge" value={currencyFormatter.format(shipment.totalCharge)} />
              </div>

              <div className="space-y-2">
                <span className="text-xs text-muted-foreground">Items</span>
                <ul className="space-y-1">
                  {shipment.items.map((item) => (
                    <li key={item.id} className="flex justify-between text-sm">
                      <span>{item.description} × {item.quantity}</span>
                      <span>{currencyFormatter.format(item.unitValue)}</span>
                    </li>
                  ))}
                </ul>
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 2: Run the full test suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: both PASS.

- [ ] **Step 3: Commit**

```bash
git add src/web/src/features/shipments/components/ShipmentDetailSheet.tsx
git commit -m "feat(web): add shipment detail Sheet"
```

---

### Task 10: Wire into staff layout; final verification; close out Phase F3

**Files:**
- Modify: `src/web/src/app/(staff)/layout.tsx`
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: everything from Tasks 1-9.
- Produces: the complete Phase F3 feature — this task's exit criteria is the phase's exit
  criteria.

- [ ] **Step 1: Mount the shipment Sheets globally**

Modify `src/web/src/app/(staff)/layout.tsx`:

```typescript
import type { ReactNode } from "react";
import { AppShell } from "@/components/layout/AppShell";
import { BranchCreateSheet } from "@/features/branches/components/BranchCreateSheet";
import { BranchDetailSheet } from "@/features/branches/components/BranchDetailSheet";
import { ShipmentCreateSheet } from "@/features/shipments/components/ShipmentCreateSheet";
import { ShipmentDetailSheet } from "@/features/shipments/components/ShipmentDetailSheet";

export default function StaffLayout({ children }: { children: ReactNode }) {
  return (
    <AppShell>
      {children}
      <BranchCreateSheet />
      <BranchDetailSheet />
      <ShipmentCreateSheet />
      <ShipmentDetailSheet />
    </AppShell>
  );
}
```

- [ ] **Step 2: Run the full frontend test suite**

Run: `cd src/web && npm test`
Expected: PASS — every test added across Tasks 1-10.

- [ ] **Step 3: Run the frontend build**

Run: `cd src/web && npm run build`
Expected: build succeeds with no type errors; `/shipments` appears in the route list.

- [ ] **Step 4: Manual end-to-end verification**

Start Postgres/API (reusing the shared containers) and the frontend dev server. In a browser, as a
registered org owner:

1. Navigate to `/shipments` from the sidebar. Confirm the list renders (empty state on a fresh
   org) and "New Shipment" is visible.
2. Click "New Shipment". Confirm it opens as an instant right-side Sheet (not a page navigation —
   URL stays `/shipments`), matching the Branches precedent.
3. Fill the form: pick a merchant (create one first via the API directly if none exist, mirroring
   Phase F2's approach for delivery zones), origin/destination branch, service type, weight, at
   least one item, recipient/sender (confirm both `GeographyPicker` instances cascade correctly
   and independently — picking a recipient province must not affect the sender's district
   options). Confirm the price estimate appears and updates a moment after the route/package
   fields are filled (debounced, not instant per keystroke).
4. Submit. Confirm it creates the shipment and switches straight to the detail Sheet for the new
   AWB (no page navigation).
5. Confirm the detail Sheet shows status (as a `StatusBadge`), route, recipient/sender, financial
   summary, and items — matching what `GET /api/v1/shipments/{awb}` actually returned.
6. Navigate back to the list, confirm the new shipment appears with correct AWB/recipient/status/
   service type/COD/created-at, and that clicking its row reopens the same detail Sheet.
7. Confirm pagination controls work if more than one page's worth of shipments exist (`Previous`/
   `Next`); confirm there's no filter UI (matches the descoped design).

- [ ] **Step 5: Update the roadmap status**

In `docs/FRONTEND-ROADMAP.md`, replace Phase F3's `*Status: Not started.*` line with a completion
note in the same style as Phases F0/F1/F2's, written only after Step 4's manual verification
actually passes — including a note that the tracking timeline and list filters are deliberately
absent (per `docs/CLARIFICATIONS.md` §14) and a Cancel action was deliberately deferred (§7 of the
design spec).

- [ ] **Step 6: Commit**

```bash
git add "src/web/src/app/(staff)/layout.tsx" docs/FRONTEND-ROADMAP.md
git commit -m "feat(web): wire shipment sheets into staff layout; close out Phase F3"
```
