# Frontend Phase F1 — Auth Flows + Organization Dashboard — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Phase F0's `StatusBadge` casing bug, add a session-expiry re-authentication dialog that resumes the page in place instead of navigating away, and replace the dashboard placeholder with real data from `GET /api/v1/reports/dashboard/organization`.

**Architecture:** A shared `clientFetch` wrapper throws a typed `SessionExpiredError` on any BFF-route `401`; a global TanStack Query `QueryCache.onError` catches it and flips a Zustand flag that a shadcn `Dialog`-based `ReauthDialog` (mounted once in `AppShell`) watches. The dashboard is a thin BFF proxy route + one TanStack Query hook + a handful of shadcn `Card`-based stat tiles, all composed in `app/(staff)/dashboard/page.tsx`.

**Tech Stack:** Same as Phase F0 (Next.js 14.2 App Router, TanStack Query 5, Zustand, shadcn/ui, Vitest + Testing Library) plus two new shadcn components this phase adds: `Dialog` and `Spinner`.

**Spec:** `docs/superpowers/specs/2026-08-29-frontend-phase-f1-auth-flows-org-dashboard-design.md`. Also see `docs/superpowers/specs/2026-08-29-frontend-roadmap-design.md` (Phase F0's still-binding conventions) and `docs/ARCHITECTURE.md` §4 (shadcn-blocks-first convention, added after Phase F0).

## Global Constraints

* Every client-side data hook calls same-origin `/api/*` BFF routes through `clientFetch`, never a raw `fetch` — this is what makes session-expiry detection singular and shared (spec §3).
* `useSession` (`src/web/src/features/auth/hooks/useSession.ts`) is **not** modified this phase — its existing "401 → return null" handling is correct for the not-yet-logged-in case and must not be rerouted through `SessionExpiredError`/the reauth dialog.
* Prefer shadcn's official components over hand-built ones (`docs/ARCHITECTURE.md` §4): this phase's `Dialog` and `Spinner` come from `npx shadcn add`, not custom implementations.
* `LoginForm` is not touched this phase (spec §5) — `ReauthDialog` is a new, separate component.
* `StatusBadge`'s public interface (`resolveStatusConfig`, `STATUS_CONFIG`'s shape, token names) does not change — only the map's *keys* are corrected.

---

### Task 1: Fix `StatusBadge` status-key casing

**Files:**
- Modify: `src/web/src/components/status-badge/statusConfig.ts`
- Modify: `src/web/src/components/status-badge/StatusBadge.test.tsx`

**Interfaces:**
- No signature changes. `resolveStatusConfig(status: string): StatusConfigEntry` still takes the raw status string from the API and returns `{ label, token }` — only the recognized key strings change from `SCREAMING_SNAKE_CASE` to the real `ShipmentStatus` enum member names.

- [ ] **Step 1: Write the failing test**

Replace the two tests in `src/web/src/components/status-badge/StatusBadge.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusBadge } from "./StatusBadge";

describe("StatusBadge", () => {
  it("renders the mapped label for a real backend status (PascalCase wire format)", () => {
    render(<StatusBadge status="InTransit" />);
    expect(screen.getByText("In Transit")).toBeInTheDocument();
  });

  it("renders the mapped label for every real ShipmentStatus enum member", () => {
    const cases: Array<[string, string]> = [
      ["Created", "Created"],
      ["PickupAssigned", "Created"],
      ["PickedUp", "Picked Up"],
      ["AtOriginBranch", "In Transit"],
      ["Sorted", "In Transit"],
      ["AtDestinationBranch", "In Transit"],
      ["OutForDelivery", "Out for Delivery"],
      ["Delivered", "Delivered"],
      ["DeliveryFailed", "Delivery Failed"],
      ["NdrCreated", "NDR"],
      ["RtoInitiated", "RTO Initiated"],
      ["RtoInTransit", "RTO In Transit"],
      ["RtoAtOrigin", "RTO At Origin"],
      ["RtoDelivered", "RTO Delivered"],
      ["Cancelled", "Cancelled"],
    ];
    for (const [status, label] of cases) {
      const { unmount } = render(<StatusBadge status={status} />);
      expect(screen.getByText(label)).toBeInTheDocument();
      unmount();
    }
  });

  it("falls back to the raw status string for a genuinely unknown status", () => {
    render(<StatusBadge status="SomeFutureStatus" />);
    expect(screen.getByText("SomeFutureStatus")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- StatusBadge.test.tsx`
Expected: FAIL — every real status name falls through to the raw-string fallback, since `STATUS_CONFIG`'s keys are currently `CREATED`, `IN_TRANSIT`, etc., not `Created`, `InTransit`.

- [ ] **Step 3: Fix the keys**

Replace `STATUS_CONFIG` in `src/web/src/components/status-badge/statusConfig.ts` (only the keys change; every `label`/`token` value is unchanged from Phase F0):

```typescript
export const STATUS_CONFIG: Record<string, StatusConfigEntry> = {
  Created: { label: "Created", token: "status-created" },
  PickupAssigned: { label: "Created", token: "status-created" },
  PickedUp: { label: "Picked Up", token: "status-in-transit" },
  AtOriginBranch: { label: "In Transit", token: "status-in-transit" },
  Sorted: { label: "In Transit", token: "status-in-transit" },
  InTransit: { label: "In Transit", token: "status-in-transit" },
  AtDestinationBranch: { label: "In Transit", token: "status-in-transit" },
  OutForDelivery: { label: "Out for Delivery", token: "status-out-for-delivery" },
  Delivered: { label: "Delivered", token: "status-delivered" },
  DeliveryFailed: { label: "Delivery Failed", token: "status-delivery-failed" },
  NdrCreated: { label: "NDR", token: "status-ndr" },
  RtoInitiated: { label: "RTO Initiated", token: "status-rto" },
  RtoInTransit: { label: "RTO In Transit", token: "status-rto" },
  RtoAtOrigin: { label: "RTO At Origin", token: "status-rto" },
  RtoDelivered: { label: "RTO Delivered", token: "status-rto" },
  Cancelled: { label: "Cancelled", token: "status-cancelled" },
};
```

Note: `PickupAssigned` now maps to the label "Created" (both pre-pickup states read the same to a
user glancing at a badge) while keeping its own key, since `AtOriginBranch`/`Sorted`/`InTransit`/
`AtDestinationBranch` all collapse to "In Transit" the same way — this matches the funnel-bucketing
groups Task 8 uses, so the badge and the funnel never disagree about which raw status means what.

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- StatusBadge.test.tsx`
Expected: PASS (3 tests, one covering all 15 real enum members).

- [ ] **Step 5: Run the full frontend suite to check for regressions**

Run: `cd src/web && npm test`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/components/status-badge/statusConfig.ts src/web/src/components/status-badge/StatusBadge.test.tsx
git commit -m "fix(web): correct StatusBadge status keys to match backend's PascalCase wire format"
```

---

### Task 2: `clientFetch` wrapper + `SessionExpiredError`

**Files:**
- Create: `src/web/src/lib/api/clientFetch.ts`
- Test: `src/web/src/lib/api/clientFetch.test.ts`

**Interfaces:**
- Produces: `class SessionExpiredError extends Error`; `clientFetch<T>(path: string, init?: RequestInit): Promise<T>`. Every later task's client-side hook (Task 7's `useOrgDashboard`, and every future phase's feature hooks) uses this instead of raw `fetch`.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/lib/api/clientFetch.test.ts`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { clientFetch, SessionExpiredError } from "./clientFetch";

describe("clientFetch", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("returns the parsed JSON body on success", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ hello: "world" }),
    });

    const result = await clientFetch<{ hello: string }>("/api/example");

    expect(result).toEqual({ hello: "world" });
    expect(global.fetch).toHaveBeenCalledWith("/api/example", undefined);
  });

  it("throws SessionExpiredError on a 401", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => null,
    });

    await expect(clientFetch("/api/example")).rejects.toBeInstanceOf(SessionExpiredError);
  });

  it("throws a plain Error on any other non-OK status", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => null,
    });

    await expect(clientFetch("/api/example")).rejects.toThrow("Request to /api/example failed with 500");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- clientFetch.test.ts`
Expected: FAIL — `./clientFetch` does not exist yet.

- [ ] **Step 3: Implement**

Create `src/web/src/lib/api/clientFetch.ts`:

```typescript
export class SessionExpiredError extends Error {
  constructor() {
    super("Session expired");
    this.name = "SessionExpiredError";
  }
}

export async function clientFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  if (response.status === 401) {
    throw new SessionExpiredError();
  }
  if (!response.ok) {
    throw new Error(`Request to ${path} failed with ${response.status}`);
  }
  return response.json();
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- clientFetch.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/lib/api/clientFetch.ts src/web/src/lib/api/clientFetch.test.ts
git commit -m "feat(web): add clientFetch wrapper with typed SessionExpiredError"
```

---

### Task 3: Session-expiry detection — `authStore` + global `QueryCache` wiring

**Files:**
- Create: `src/web/src/lib/store/authStore.ts`
- Modify: `src/web/src/app/providers.tsx`
- Test: `src/web/src/app/providers.test.tsx`

**Interfaces:**
- Consumes: `SessionExpiredError` (Task 2).
- Produces: `useAuthStore()` with `sessionExpired: boolean`, `showReauthDialog(): void`,
  `hideReauthDialog(): void`. Task 4's `ReauthDialog` reads `sessionExpired` and calls
  `hideReauthDialog()` on successful re-auth.

- [ ] **Step 1: `authStore`**

Create `src/web/src/lib/store/authStore.ts`:

```typescript
import { create } from "zustand";

interface AuthState {
  sessionExpired: boolean;
  showReauthDialog: () => void;
  hideReauthDialog: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  sessionExpired: false,
  showReauthDialog: () => set({ sessionExpired: true }),
  hideReauthDialog: () => set({ sessionExpired: false }),
}));
```

- [ ] **Step 2: Write the failing test for the wiring**

Create `src/web/src/app/providers.test.tsx`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { useQuery } from "@tanstack/react-query";
import { Providers } from "./providers";
import { SessionExpiredError } from "@/lib/api/clientFetch";
import { useAuthStore } from "@/lib/store/authStore";

function ThrowingQuery() {
  useQuery({
    queryKey: ["throws-session-expired"],
    queryFn: () => {
      throw new SessionExpiredError();
    },
    retry: false,
  });
  return null;
}

describe("Providers", () => {
  beforeEach(() => {
    useAuthStore.setState({ sessionExpired: false });
  });

  it("flips authStore.sessionExpired when a query throws SessionExpiredError", async () => {
    render(
      <Providers>
        <ThrowingQuery />
      </Providers>
    );

    await waitFor(() => expect(useAuthStore.getState().sessionExpired).toBe(true));
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- providers.test.tsx`
Expected: FAIL — `providers.tsx`'s `QueryClient` has no `queryCache`/`onError` wiring yet, so
`sessionExpired` never flips.

- [ ] **Step 4: Wire the global error handler**

Replace `src/web/src/app/providers.tsx`:

```typescript
"use client";

import { QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState } from "react";
import { SessionExpiredError } from "@/lib/api/clientFetch";
import { useAuthStore } from "@/lib/store/authStore";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        queryCache: new QueryCache({
          onError: (error) => {
            if (error instanceof SessionExpiredError) {
              useAuthStore.getState().showReauthDialog();
            }
          },
        }),
      })
  );

  return (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- providers.test.tsx`
Expected: PASS.

- [ ] **Step 6: Run the full frontend suite**

Run: `cd src/web && npm test`
Expected: PASS, no regressions (Phase F0's `useSession`/`usePermission` tests mock `fetch` directly
and never throw `SessionExpiredError`, so they're unaffected by the new `queryCache`).

- [ ] **Step 7: Commit**

```bash
git add src/web/src/lib/store/authStore.ts src/web/src/app/providers.tsx src/web/src/app/providers.test.tsx
git commit -m "feat(web): wire global SessionExpiredError detection into TanStack Query's QueryCache"
```

---

### Task 4: `ReauthDialog`

**Files:**
- Create (via `npx shadcn add dialog spinner`): `src/web/src/components/ui/dialog.tsx`, `src/web/src/components/ui/spinner.tsx`
- Create: `src/web/src/components/layout/ReauthDialog.tsx`
- Test: `src/web/src/components/layout/ReauthDialog.test.tsx`

**Interfaces:**
- Consumes: `useAuthStore` (Task 3), `useSession` (Phase F0, for the last-known email — read only,
  never modified), `POST /api/auth/login` and `POST /api/auth/logout` (Phase F0 BFF routes).
- Produces: `<ReauthDialog />`, a self-contained component with no props — Task 5 mounts it once in
  `AppShell`.

- [ ] **Step 1: Add the shadcn components**

```bash
cd src/web
npx shadcn add dialog spinner --yes
```

Expected: `src/web/src/components/ui/dialog.tsx` and `src/web/src/components/ui/spinner.tsx` are
created.

- [ ] **Step 2: `ReauthDialog`**

Create `src/web/src/components/layout/ReauthDialog.tsx`:

```typescript
"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { Lock } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { useAuthStore } from "@/lib/store/authStore";
import { useSession } from "@/features/auth/hooks/useSession";

export function ReauthDialog() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const sessionExpired = useAuthStore((s) => s.sessionExpired);
  const hideReauthDialog = useAuthStore((s) => s.hideReauthDialog);
  const { data: session } = useSession();

  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: session?.email ?? "", password }),
    });
    setSubmitting(false);
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setError(body.error ?? "Sign-in failed.");
      return;
    }
    setPassword("");
    hideReauthDialog();
    await queryClient.invalidateQueries();
  }

  async function handleSignInAsSomeoneElse() {
    await fetch("/api/auth/logout", { method: "POST" });
    hideReauthDialog();
    router.push("/login");
  }

  return (
    <Dialog open={sessionExpired}>
      <DialogContent
        onInteractOutside={(e) => e.preventDefault()}
        onEscapeKeyDown={(e) => e.preventDefault()}
        hideCloseButton
      >
        <DialogHeader>
          <DialogTitle className="font-display">Session expired</DialogTitle>
          <DialogDescription>
            Sign in again to keep going — you won&apos;t lose your place on this page.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="reauth-email">Email</Label>
            <Input id="reauth-email" type="email" value={session?.email ?? ""} disabled />
          </div>
          <div className="space-y-2">
            <Label htmlFor="reauth-password">Password</Label>
            <div className="relative">
              <Lock
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="reauth-password"
                type="password"
                autoComplete="current-password"
                className="pl-9"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoFocus
              />
            </div>
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter className="flex-col gap-2 sm:flex-col">
            <Button type="submit" className="w-full" disabled={submitting}>
              {submitting ? <Spinner className="h-4 w-4" /> : "Sign in"}
            </Button>
            <Button
              type="button"
              variant="ghost"
              className="w-full"
              onClick={handleSignInAsSomeoneElse}
            >
              Sign in as someone else
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
```

Note: `hideCloseButton` is a prop this task adds to the shared `DialogContent` (Step 3) — the
generated shadcn `dialog.tsx` always renders a corner close (X) button, which would let a user
dismiss a session-expired dialog without resolving it, contradicting the "only two ways out"
design (spec §3).

- [ ] **Step 3: Add the `hideCloseButton` escape hatch to the generated `DialogContent`**

Open `src/web/src/components/ui/dialog.tsx` (just generated by Step 1) and find the
`DialogContent` component. Add a `hideCloseButton?: boolean` prop and wrap the existing close
`<DialogPrimitive.Close>` button block in `{!hideCloseButton && (...)}`. The exact generated code
varies by shadcn CLI version, so there's no single verbatim diff to paste here — locate the
`<DialogPrimitive.Close ...>` JSX block (it contains an `XIcon` or similar) inside
`DialogContent`'s `forwardRef` body, and:

1. Add `hideCloseButton?: boolean` to the props type/destructuring for `DialogContent`.
2. Wrap that `<DialogPrimitive.Close>` block in `{!hideCloseButton && ( ... )}`.

- [ ] **Step 4: Write the test**

Create `src/web/src/components/layout/ReauthDialog.test.tsx`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReauthDialog } from "./ReauthDialog";
import { useAuthStore } from "@/lib/store/authStore";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ReauthDialog />
    </QueryClientProvider>
  );
}

describe("ReauthDialog", () => {
  beforeEach(() => {
    useAuthStore.setState({ sessionExpired: false });
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => null,
    });
  });

  it("is hidden when the session has not expired", () => {
    renderDialog();
    expect(screen.queryByText("Session expired")).not.toBeInTheDocument();
  });

  it("shows when authStore.sessionExpired is true", async () => {
    useAuthStore.setState({ sessionExpired: true });
    renderDialog();
    expect(await screen.findByText("Session expired")).toBeInTheDocument();
  });

  it("hides authStore.sessionExpired on successful re-auth", async () => {
    useAuthStore.setState({ sessionExpired: true });
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (url === "/api/auth/login") {
        return Promise.resolve({ ok: true, status: 200, json: async () => ({ ok: true }) });
      }
      return Promise.resolve({ ok: false, status: 401, json: async () => null });
    });
    renderDialog();

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: /^sign in$/i }));

    await waitFor(() => expect(useAuthStore.getState().sessionExpired).toBe(false));
  });
});
```

- [ ] **Step 5: Run the test**

Run: `cd src/web && npm test -- ReauthDialog.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/components/ui/dialog.tsx src/web/src/components/ui/spinner.tsx src/web/src/components/layout/ReauthDialog.tsx src/web/src/components/layout/ReauthDialog.test.tsx
git commit -m "feat(web): add ReauthDialog for session-expiry recovery"
```

---

### Task 5: Mount `ReauthDialog` in `AppShell`

**Files:**
- Modify: `src/web/src/components/layout/AppShell.tsx`

**Interfaces:**
- Consumes: `ReauthDialog` (Task 4).

- [ ] **Step 1: Mount it**

Modify `src/web/src/components/layout/AppShell.tsx` to render `<ReauthDialog />` once, alongside
the existing shell markup:

```typescript
import type { ReactNode } from "react";
import { TopNav } from "@/components/layout/TopNav";
import { Sidebar } from "@/components/layout/Sidebar";
import { ReauthDialog } from "@/components/layout/ReauthDialog";

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex h-screen flex-col">
      <TopNav />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar />
        <main className="flex-1 overflow-y-auto bg-background p-6">{children}</main>
      </div>
      <ReauthDialog />
    </div>
  );
}
```

- [ ] **Step 2: Run the full frontend suite and build**

Run: `cd src/web && npm test && npm run build`
Expected: PASS / build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/web/src/components/layout/AppShell.tsx
git commit -m "feat(web): mount ReauthDialog in the staff app shell"
```

---

### Task 6: Dashboard BFF route

**Files:**
- Create: `src/web/src/app/api/reports/dashboard/organization/route.ts`
- Test: `src/web/src/app/api/reports/dashboard/organization/route.test.ts`

**Interfaces:**
- Consumes: `apiFetch` (Phase F0, `src/web/src/lib/auth/apiFetch.ts`).
- Produces: `GET /api/reports/dashboard/organization` → proxies
  `GET {API}/api/v1/reports/dashboard/organization`, returning the backend's JSON body verbatim on
  success, `401` on an unauthenticated/expired session. Task 8's `useOrgDashboard` hook is the only
  consumer.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/app/api/reports/dashboard/organization/route.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/reports/dashboard/organization", () => {
  it("proxies the backend response body on success", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ todaysShipmentsByStatus: { Created: 3 } }),
    } as Response);

    const response = await GET();

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ todaysShipmentsByStatus: { Created: 3 } });
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });

  it("propagates the backend's own non-OK status", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => ({ error: "forbidden" }),
    } as Response);

    const response = await GET();

    expect(response.status).toBe(403);
  });
});
```

Note: `NextRequest` is imported but unused in this file's tests since `GET` here takes no request
parameter (no query params needed, per spec §4) — remove the unused import if your editor flags it
before committing.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd src/web && npm test -- route.test.ts`
Expected: FAIL — `./route` does not exist yet.

- [ ] **Step 3: Implement**

Create `src/web/src/app/api/reports/dashboard/organization/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/reports/dashboard/organization");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd src/web && npm test -- route.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/api/reports/dashboard/organization
git commit -m "feat(web): add organization dashboard BFF proxy route"
```

---

### Task 7: `useOrgDashboard` hook

**Files:**
- Create: `src/web/src/features/reports/types.ts`
- Create: `src/web/src/features/reports/hooks/useOrgDashboard.ts`
- Test: `src/web/src/features/reports/hooks/useOrgDashboard.test.ts`

**Interfaces:**
- Consumes: `clientFetch` (Task 2), `GET /api/reports/dashboard/organization` (Task 6).
- Produces: `OrgDashboardSummary` type; `useOrgDashboard(): UseQueryResult<OrgDashboardSummary>`.
  Task 8's tile components are the only consumers.

- [ ] **Step 1: Types**

Create `src/web/src/features/reports/types.ts`:

```typescript
export interface CodFinancialSummary {
  expected: number;
  collected: number;
  pending: number;
  settled: number;
  merchantPayable: number;
}

export interface OrgDashboardSummary {
  todaysShipmentsByStatus: Record<string, number>;
  financial: CodFinancialSummary;
  overallDeliverySuccessRate: number | null;
  overallNdrRate: number | null;
  overallRtoRate: number | null;
  // .NET TimeSpan's default JSON format, e.g. "02:15:30" (hh:mm:ss) or "1.02:15:30" (d.hh:mm:ss)
  averageDeliveryTime: string | null;
}
```

- [ ] **Step 2: Write the failing test**

Create `src/web/src/features/reports/hooks/useOrgDashboard.test.ts`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useOrgDashboard } from "./useOrgDashboard";

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("useOrgDashboard", () => {
  it("fetches from the BFF proxy route and returns the parsed summary", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        todaysShipmentsByStatus: { Created: 2, Delivered: 5 },
        financial: { expected: 100, collected: 80, pending: 20, settled: 60, merchantPayable: 20 },
        overallDeliverySuccessRate: 0.92,
        overallNdrRate: 0.05,
        overallRtoRate: 0.02,
        averageDeliveryTime: "02:15:30",
      }),
    });

    const { result } = renderHook(() => useOrgDashboard(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.financial.expected).toBe(100);
    expect(global.fetch).toHaveBeenCalledWith("/api/reports/dashboard/organization", undefined);
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/web && npm test -- useOrgDashboard.test.ts`
Expected: FAIL — `./useOrgDashboard` does not exist yet.

- [ ] **Step 4: Implement**

Create `src/web/src/features/reports/hooks/useOrgDashboard.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { OrgDashboardSummary } from "@/features/reports/types";

export function useOrgDashboard() {
  return useQuery({
    queryKey: ["org-dashboard"],
    queryFn: () => clientFetch<OrgDashboardSummary>("/api/reports/dashboard/organization"),
  });
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd src/web && npm test -- useOrgDashboard.test.ts`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/reports/types.ts src/web/src/features/reports/hooks/useOrgDashboard.ts src/web/src/features/reports/hooks/useOrgDashboard.test.ts
git commit -m "feat(web): add useOrgDashboard hook"
```

---

### Task 8: Dashboard content components

**Files:**
- Create: `src/web/src/features/reports/components/ShipmentFunnel.tsx`
- Create: `src/web/src/features/reports/components/FinancialSummary.tsx`
- Create: `src/web/src/features/reports/components/RateTiles.tsx`
- Test: `src/web/src/features/reports/components/ShipmentFunnel.test.tsx`

**Interfaces:**
- Consumes: `OrgDashboardSummary` (Task 7), `StatusBadge`'s status-token grouping (Task 1) for the
  funnel's bucket colors.
- Produces: `<ShipmentFunnel data={Record<string, number>} />`, `<FinancialSummary data={CodFinancialSummary} />`,
  `<RateTiles data={{ overallDeliverySuccessRate, overallNdrRate, overallRtoRate, averageDeliveryTime }} />`.
  Task 9 composes all three on the dashboard page.

This task's exact tile visual treatment (spacing, number emphasis, grid layout) is this phase's
own design work per spec §4 — before writing the component code below, invoke the `dataviz` skill
for guidance on stat-tile/KPI-row presentation, then implement following its guidance while keeping
the data contract (props, bucket grouping, formatting rules) exactly as specified here. The JSX
below is a reasonable baseline if the skill's guidance doesn't suggest changes; treat its structure
as a starting point, not a mandate, but keep every named CSS custom property/token from Phase F0
(no new colors invented ad hoc) and the exact bucket-to-status mapping from spec §4.

- [ ] **Step 1: Invoke the `dataviz` skill**

Ask it for guidance on presenting: (a) a 7-8 bucket status funnel/breakdown reusing the existing
`status-*` design tokens, (b) a 5-value currency summary row, (c) 3 percentage + 1 duration KPI
tiles — all inside an existing app shell's content area (not a standalone page), consistent with
Phase F0's established Card-based, icon-lucide, ink/paper palette. Apply its guidance to the three
components below.

- [ ] **Step 2: `ShipmentFunnel`**

Create `src/web/src/features/reports/components/ShipmentFunnel.tsx`:

```typescript
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

const FUNNEL_BUCKETS: Array<{ label: string; token: string; statuses: string[] }> = [
  { label: "Created", token: "status-created", statuses: ["Created", "PickupAssigned"] },
  { label: "Picked Up", token: "status-in-transit", statuses: ["PickedUp"] },
  {
    label: "In Transit",
    token: "status-in-transit",
    statuses: ["AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch"],
  },
  { label: "Out for Delivery", token: "status-out-for-delivery", statuses: ["OutForDelivery"] },
  { label: "Delivered", token: "status-delivered", statuses: ["Delivered"] },
  { label: "NDR", token: "status-ndr", statuses: ["DeliveryFailed", "NdrCreated"] },
  {
    label: "RTO",
    token: "status-rto",
    statuses: ["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"],
  },
  { label: "Cancelled", token: "status-cancelled", statuses: ["Cancelled"] },
];

export function ShipmentFunnel({ data }: { data: Record<string, number> }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-display text-base">Today&apos;s Shipments</CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {FUNNEL_BUCKETS.map((bucket) => {
          const count = bucket.statuses.reduce((sum, status) => sum + (data[status] ?? 0), 0);
          return (
            <div key={bucket.label} className="flex flex-col gap-1">
              <div className="flex items-center gap-1.5">
                <span
                  className="h-2 w-2 rounded-full"
                  style={{ backgroundColor: `hsl(var(--${bucket.token}))` }}
                  aria-hidden="true"
                />
                <span className="text-xs text-muted-foreground">{bucket.label}</span>
              </div>
              <span className="font-display text-2xl font-semibold">{count}</span>
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 3: `FinancialSummary`**

Create `src/web/src/features/reports/components/FinancialSummary.tsx`:

```typescript
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { CodFinancialSummary } from "@/features/reports/types";

function formatCurrency(value: number): string {
  return new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" }).format(value);
}

const FIELDS: Array<{ key: keyof CodFinancialSummary; label: string }> = [
  { key: "expected", label: "Expected" },
  { key: "collected", label: "Collected" },
  { key: "pending", label: "Pending" },
  { key: "settled", label: "Settled" },
  { key: "merchantPayable", label: "Merchant Payable" },
];

export function FinancialSummary({ data }: { data: CodFinancialSummary }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-display text-base">COD Summary</CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-4 sm:grid-cols-5">
        {FIELDS.map((field) => (
          <div key={field.key} className="flex flex-col gap-1">
            <span className="text-xs text-muted-foreground">{field.label}</span>
            <span className="font-mono text-lg font-semibold">{formatCurrency(data[field.key])}</span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 4: `RateTiles`**

Create `src/web/src/features/reports/components/RateTiles.tsx`:

```typescript
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

function formatPercent(value: number | null): string {
  return value === null ? "—" : `${(value * 100).toFixed(1)}%`;
}

// .NET TimeSpan's default JSON string, e.g. "02:15:30" or "1.02:15:30" (d.hh:mm:ss)
function formatDuration(value: string | null): string {
  if (value === null) return "—";
  const [dayPart, timePart] = value.includes(".") ? value.split(".") : [null, value];
  const [hh, mm] = timePart.split(":");
  const hours = (dayPart ? parseInt(dayPart, 10) * 24 : 0) + parseInt(hh, 10);
  return `${hours}h ${parseInt(mm, 10)}m`;
}

export function RateTiles({
  data,
}: {
  data: {
    overallDeliverySuccessRate: number | null;
    overallNdrRate: number | null;
    overallRtoRate: number | null;
    averageDeliveryTime: string | null;
  };
}) {
  const tiles = [
    { label: "Delivery Success Rate", value: formatPercent(data.overallDeliverySuccessRate) },
    { label: "NDR Rate", value: formatPercent(data.overallNdrRate) },
    { label: "RTO Rate", value: formatPercent(data.overallRtoRate) },
    { label: "Avg. Delivery Time", value: formatDuration(data.averageDeliveryTime) },
  ];
  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
      {tiles.map((tile) => (
        <Card key={tile.label}>
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-normal text-muted-foreground">{tile.label}</CardTitle>
          </CardHeader>
          <CardContent>
            <span className="font-display text-2xl font-semibold">{tile.value}</span>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
```

- [ ] **Step 5: Write a test for the funnel's bucketing (the piece with real logic)**

Create `src/web/src/features/reports/components/ShipmentFunnel.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ShipmentFunnel } from "./ShipmentFunnel";

describe("ShipmentFunnel", () => {
  it("sums multiple raw statuses into one bucket", () => {
    render(
      <ShipmentFunnel
        data={{
          AtOriginBranch: 2,
          Sorted: 1,
          InTransit: 4,
          AtDestinationBranch: 0,
          Delivered: 10,
        }}
      />
    );

    const inTransitLabel = screen.getByText("In Transit");
    const inTransitCount = inTransitLabel.parentElement?.nextElementSibling;
    expect(inTransitCount?.textContent).toBe("7");
    expect(screen.getByText("10")).toBeInTheDocument();
  });

  it("renders 0 for a bucket with no matching statuses in the data", () => {
    render(<ShipmentFunnel data={{ Delivered: 3 }} />);
    const createdLabel = screen.getByText("Created");
    const createdCount = createdLabel.parentElement?.nextElementSibling;
    expect(createdCount?.textContent).toBe("0");
  });
});
```

- [ ] **Step 6: Run the test**

Run: `cd src/web && npm test -- ShipmentFunnel.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/reports/components
git commit -m "feat(web): add dashboard stat-tile components (funnel, financial, rates)"
```

---

### Task 9: Wire the dashboard page; final exit-criteria verification

**Files:**
- Modify: `src/web/src/app/(staff)/dashboard/page.tsx`
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: everything from Tasks 1-8.
- Produces: the running dashboard — this task's exit criteria is the phase's exit criteria.

- [ ] **Step 1: Replace the placeholder**

Replace `src/web/src/app/(staff)/dashboard/page.tsx`:

```typescript
"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useOrgDashboard } from "@/features/reports/hooks/useOrgDashboard";
import { ShipmentFunnel } from "@/features/reports/components/ShipmentFunnel";
import { FinancialSummary } from "@/features/reports/components/FinancialSummary";
import { RateTiles } from "@/features/reports/components/RateTiles";

export default function DashboardPage() {
  const { data, isLoading, isError, refetch } = useOrgDashboard();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load the dashboard"
        description="Something went wrong fetching today's summary."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6">
      <ShipmentFunnel data={data.todaysShipmentsByStatus} />
      <FinancialSummary data={data.financial} />
      <RateTiles
        data={{
          overallDeliverySuccessRate: data.overallDeliverySuccessRate,
          overallNdrRate: data.overallNdrRate,
          overallRtoRate: data.overallRtoRate,
          averageDeliveryTime: data.averageDeliveryTime,
        }}
      />
    </div>
  );
}
```

- [ ] **Step 2: Run the full frontend test suite**

Run: `cd src/web && npm test`
Expected: PASS — every test added across Tasks 1-9.

- [ ] **Step 3: Run the frontend build**

Run: `cd src/web && npm run build`
Expected: build succeeds with no type errors.

- [ ] **Step 4: Manual end-to-end verification**

Start Postgres/API (`docker compose up -d api`, reusing the already-running shared containers) and
the frontend dev server (`cd src/web && npm run dev`). In a browser:

1. Log in with a registered org owner. Confirm the dashboard renders real data — a shipment
   funnel (likely all zeros for a fresh test org; book a shipment first via the API if you want
   non-zero counts to look at), a COD summary row, and the four rate tiles.
2. Verify the loading state: throttle the network (or add a temporary artificial delay to the BFF
   route) and confirm the `Spinner` renders before data arrives, then remove the temporary delay.
3. Verify session-expiry recovery: after logging in, manually clear the `courier_refresh_token`
   cookie via DevTools (Application > Cookies) while leaving `courier_access_token` in place, wait
   15 minutes for the access token to expire (or manually clear both cookies to simulate the
   already-expired case directly), then trigger a dashboard refetch (e.g. switch browser tabs and
   back, or click somewhere that refetches). Confirm the `ReauthDialog` appears instead of a
   redirect, the email field is prefilled, and submitting the correct password dismisses the
   dialog and the dashboard data loads without a page navigation. This is the concrete proof of
   `docs/FRONTEND-ROADMAP.md`'s Phase F1 exit criteria language ("session expiry redirects to
   login without a full page-state loss surprise") — actually resolved here as *no redirect at
   all*, an even better outcome than the roadmap text's original framing, per the user's explicit
   direction during this phase's brainstorm.
4. Verify "Sign in as someone else" from the re-auth dialog logs out and lands on `/login`.

- [ ] **Step 5: Update the roadmap status**

In `docs/FRONTEND-ROADMAP.md`, replace Phase F1's `*Status: Not started.*` line with a completion
note in the same style as Phase F0's, written only after Step 4's manual verification actually
passes.

- [ ] **Step 6: Commit**

```bash
git add "src/web/src/app/(staff)/dashboard/page.tsx" docs/FRONTEND-ROADMAP.md
git commit -m "feat(web): wire real organization dashboard data; close out Phase F1"
```
