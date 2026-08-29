# Frontend Phase F0 — Design System & App Shell — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the staff back-office's visual foundation and app shell — design tokens, themed shadcn/ui primitives, the BFF auth layer, permission-aware navigation, and the shared components (data table, status badge, empty/error/loading states) every later frontend phase (F1–F9) composes.

**Architecture:** One Next.js app (`src/web`, App Router). Next.js Route Handlers act as a thin backend-for-frontend: they hold the real API's JWT access/refresh tokens in `httpOnly` cookies and proxy authenticated calls server-side, so no token is ever readable by client JS. Client components read session/permission state via a same-origin `/api/auth/session` call, never the tokens themselves. shadcn/ui supplies primitive components, restyled with a purpose-built token set (not shipped defaults).

**Tech Stack:** Next.js 14.2 (App Router), React 18, TypeScript, Tailwind CSS 3.4, TanStack Query 5, React Hook Form + Zod, Zustand, shadcn/ui (Radix primitives + `class-variance-authority`), Vitest + Testing Library. Backend: ASP.NET Core minimal APIs, EF Core (existing `Identity` module).

**Spec:** `docs/superpowers/specs/2026-08-29-frontend-roadmap-design.md`. Also see `docs/ARCHITECTURE.md` §4 (frontend architecture, already committed), `docs/FRONTEND-ROADMAP.md` (Phase F0 entry/exit criteria), `docs/CLARIFICATIONS.md` entry F0.1 (the `/auth/me` gap this plan's Task 1 closes).

## Global Constraints

* Tokens (access + refresh) are never stored in `localStorage` or a non-`httpOnly` cookie — spec §4.
* The frontend never computes or trusts price/COD/tenant/branch/role values from user input — CLAUDE.md §33. Permissions displayed client-side are always an echo of the `/auth/me` response, never locally computed.
* Nav items, buttons, and route access are gated through the single `usePermission` hook (spec §5) — no role-name checks anywhere in frontend code.
* Folder layout follows `ARCHITECTURE.md` §4 exactly: `app/`, `features/`, `components/`, `lib/`, `services/`.
* Zustand holds only the active-branch selector and sidebar-collapsed state (spec §6) — nothing server-derived.
* Exact color/type token values are Phase F0's own creative work product (spec §10) — Task 3 below directs the executor to invoke the `frontend-design` and `impeccable` skills to determine them; the file locations and CSS variable *names* are fixed by this plan, the *values* are not.

---

### Task 1: Backend — `GET /api/v1/auth/me`

**Files:**
- Modify: `src/Modules/Identity/Application/AuthDtos.cs`
- Modify: `src/Modules/Identity/Application/AuthService.cs`
- Modify: `src/Modules/Identity/Presentation/AuthEndpoints.cs`
- Test: `tests/Api.Tests/AuthEndpointsTests.cs`

**Interfaces:**
- Consumes: `IBranchLookupService.GetNamesByIdsAsync(IReadOnlySet<Guid>, CancellationToken)` (existing, `src/Modules/Branches/Application/IBranchLookupService.cs`, already registered in `Program.cs`).
- Produces: `GET /api/v1/auth/me` (bearer-authenticated, no extra permission) → `200 MeResponse { userId: Guid, organizationId: Guid, email: string, fullName: string, permissions: string[], hasAllBranchesScope: bool, branches: MeBranchGrant[] }` where `MeBranchGrant = { branchId: Guid, branchName: string, expiresAt: DateTimeOffset? }`. `401` if unauthenticated. Every later frontend task in this plan treats this shape as fixed.

- [ ] **Step 1: Write the failing test**

Add to `tests/Api.Tests/AuthEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task Me_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_AuthenticatedOwner_ReturnsPermissionsAndAllBranchesScope()
    {
        var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registerRequest = new RegisterOrganizationRequest(
            $"Me Org {suffix}", $"me-org-{suffix}", $"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner Person");
        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(me);
        Assert.Equal(auth.UserId, me!.UserId);
        Assert.Equal(auth.OrganizationId, me.OrganizationId);
        Assert.Equal("Owner Person", me.FullName);
        Assert.Contains("shipment.create", me.Permissions);
        Assert.True(me.HasAllBranchesScope);
        Assert.Empty(me.Branches);
    }
```

Add `using System.Net.Http.Headers;` to the test file's using block if not already present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~AuthEndpointsTests.Me_"`
Expected: build error — `MeResponse` does not exist yet.

- [ ] **Step 3: Add the DTOs**

Add to `src/Modules/Identity/Application/AuthDtos.cs`:

```csharp
public record MeBranchGrant(Guid BranchId, string BranchName, DateTimeOffset? ExpiresAt);
public record MeResponse(Guid UserId, Guid OrganizationId, string Email, string FullName, IReadOnlyList<string> Permissions, bool HasAllBranchesScope, IReadOnlyList<MeBranchGrant> Branches);
```

- [ ] **Step 4: Add `AuthService.GetMeAsync`**

In `src/Modules/Identity/Application/AuthService.cs`, add `using System.Security.Claims;` and `using Courier.SharedKernel;` to the top, add a constructor dependency, and add the method:

```csharp
    private readonly IBranchLookupService _branchLookupService;

    public AuthService(DbContext dbContext, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService, IAuditLogger auditLogger, IBranchLookupService branchLookupService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _auditLogger = auditLogger;
        _branchLookupService = branchLookupService;
    }
```

(Replace the existing constructor body with this one — same four assignments plus the new one.)

```csharp
    public async Task<MeResponse> GetMeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userId = principal.GetUserId()!.Value;
        var user = await _dbContext.Set<User>().FirstAsync(u => u.Id == userId, cancellationToken);

        var permissions = await _dbContext.Set<Role>()
            .Where(r => _dbContext.Set<UserRole>().Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .SelectMany(r => r.Permissions)
            .Select(p => p.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        var hasAllBranchesScope = await _dbContext.Set<UserRole>()
            .AnyAsync(ur => ur.UserId == userId && ur.BranchScope == BranchScope.AllBranches, cancellationToken);

        var userBranches = await _dbContext.Set<UserBranch>()
            .Where(ub => ub.UserId == userId)
            .ToListAsync(cancellationToken);

        var branchIds = userBranches.Select(ub => ub.BranchId).ToHashSet();
        var branchNames = await _branchLookupService.GetNamesByIdsAsync(branchIds, cancellationToken);

        var branches = userBranches
            .Select(ub => new MeBranchGrant(
                ub.BranchId,
                branchNames.TryGetValue(ub.BranchId, out var name) ? name : "Unknown Branch",
                ub.ExpiresAt))
            .ToList();

        return new MeResponse(user.Id, user.OrganizationId!.Value, user.Email, user.FullName, permissions, hasAllBranchesScope, branches);
    }
```

This method is added at the end of the class, before the closing brace, after `HashToken`.

- [ ] **Step 5: Add the endpoint**

In `src/Modules/Identity/Presentation/AuthEndpoints.cs`, add inside `MapAuthEndpoints`, after the `/refresh` group:

```csharp
        group.MapGet("/me", async (HttpContext httpContext, AuthService authService) =>
        {
            var result = await authService.GetMeAsync(httpContext.User);
            return Results.Ok(result);
        }).RequireAuthorization();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Api.Tests --filter "FullyQualifiedName~AuthEndpointsTests"`
Expected: all `AuthEndpointsTests` PASS, including the two new tests.

- [ ] **Step 7: Run the full backend test suite to check for regressions**

Run: `dotnet test`
Expected: PASS (no regressions — `AuthService`'s constructor gained a parameter DI already resolves via `IBranchLookupService`'s existing `Program.cs` registration; no other call site constructs `AuthService` directly).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Identity/Application/AuthDtos.cs src/Modules/Identity/Application/AuthService.cs src/Modules/Identity/Presentation/AuthEndpoints.cs tests/Api.Tests/AuthEndpointsTests.cs
git commit -m "feat(identity): add GET /api/v1/auth/me for frontend session hydration"
```

---

### Task 2: Frontend tooling foundation (shadcn/ui plumbing, Zustand)

**Files:**
- Create: `src/web/components.json`
- Create: `src/web/src/lib/utils.ts`
- Modify: `src/web/package.json` (via `npm install`)
- Modify: `src/web/tailwind.config.ts`

**Interfaces:**
- Produces: `cn(...inputs: ClassValue[]): string` at `@/lib/utils`, consumed by every shadcn primitive added in later tasks. `components.json` makes `npx shadcn@latest add <name>` work for those later tasks.

- [ ] **Step 1: Install dependencies**

```bash
cd src/web
npm install zustand class-variance-authority clsx tailwind-merge lucide-react tailwindcss-animate @radix-ui/react-slot
```

- [ ] **Step 2: Write `lib/utils.ts`**

Create `src/web/src/lib/utils.ts`:

```typescript
import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
```

- [ ] **Step 3: Write `components.json`**

Create `src/web/components.json`:

```json
{
  "$schema": "https://ui.shadcn.com/schema.json",
  "style": "new-york",
  "rsc": true,
  "tsx": true,
  "tailwind": {
    "config": "tailwind.config.ts",
    "css": "src/app/globals.css",
    "baseColor": "neutral",
    "cssVariables": true,
    "prefix": ""
  },
  "aliases": {
    "components": "@/components",
    "utils": "@/lib/utils",
    "ui": "@/components/ui",
    "lib": "@/lib",
    "hooks": "@/hooks"
  }
}
```

- [ ] **Step 4: Register the `tailwindcss-animate` plugin**

In `src/web/tailwind.config.ts`, replace `plugins: []` with:

```typescript
  plugins: [require("tailwindcss-animate")],
```

- [ ] **Step 5: Verify the app still builds**

Run: `cd src/web && npm run build`
Expected: build succeeds (no functional change yet — this task is plumbing only).

- [ ] **Step 6: Commit**

```bash
git add src/web/package.json src/web/package-lock.json src/web/components.json src/web/src/lib/utils.ts src/web/tailwind.config.ts
git commit -m "chore(web): add shadcn/ui plumbing and Zustand dependency"
```

---

### Task 3: Design tokens — color system, type scale, spacing, motion

**Files:**
- Modify: `src/web/src/app/globals.css`
- Modify: `src/web/tailwind.config.ts`

**Interfaces:**
- Consumes: `@/lib/utils` (Task 2), the existing Geist Sans/Mono font setup already wired in `src/web/src/app/layout.tsx`.
- Produces: CSS custom properties on `:root` (and a `.dark` class, even though F0 doesn't build a theme toggle — future-proofing the tokens costs nothing extra and every later phase inherits it for free) covering the standard shadcn slot names (`--background`, `--foreground`, `--card`, `--card-foreground`, `--popover`, `--popover-foreground`, `--primary`, `--primary-foreground`, `--secondary`, `--secondary-foreground`, `--muted`, `--muted-foreground`, `--accent`, `--accent-foreground`, `--destructive`, `--destructive-foreground`, `--border`, `--input`, `--ring`, `--radius`) plus **domain-specific semantic tokens** for shipment/NDR/RTO/manifest/COD status: `--status-created`, `--status-in-transit`, `--status-out-for-delivery`, `--status-delivered`, `--status-delivery-failed`, `--status-ndr`, `--status-rto`, `--status-cancelled`, each with a paired `-foreground`. `tailwind.config.ts`'s `theme.extend.colors` maps every one of these into a Tailwind color name (e.g. `status-in-transit: "hsl(var(--status-in-transit))"`) so later tasks write `bg-status-in-transit` instead of raw CSS variables. `Task 8`'s `statusConfig.ts` is the single consumer that maps a backend status string to one of these tokens — no other file re-implements that mapping.

This task's actual token *values* are this phase's core design work, not pre-decided by this plan (spec §10). Do the following as real steps, not a placeholder:

- [ ] **Step 1: Establish visual direction**

Invoke `Skill(frontend-design)` to define the product's visual identity: a Nepal domestic courier/logistics operations tool — not a generic SaaS admin template. Decide the type scale (headings/body/mono for AWB numbers), a primary/accent color pair, a neutral scale, and the 7 status colors above (each must be distinguishable at a glance and colorblind-considerate — shipment status is read quickly and often). Produce a short rationale (2-4 sentences) for the palette choice.

- [ ] **Step 2: Critique and refine**

Invoke `Skill(impeccable)` against the direction from Step 1 to audit contrast (WCAG AA for text-on-background pairs, including every status badge foreground/background pair), visual hierarchy, and that the palette avoids generic-SaaS-blue defaults. Revise token values based on the critique.

- [ ] **Step 3: Encode the tokens**

Write the finalized HSL values into `src/web/src/app/globals.css`'s `:root` block (replacing the current two-variable placeholder) using the exact variable names listed above, each as an HSL triplet without the `hsl()` wrapper (shadcn convention, e.g. `--primary: 222 47% 11%;`) so Tailwind can compose `hsl(var(--primary) / <alpha-value>)`. Add a `.dark { ... }` block with dark-mode values even though nothing toggles it yet.

- [ ] **Step 4: Wire into Tailwind**

In `src/web/tailwind.config.ts`, extend `theme.extend.colors` with every token from Step 3 (standard shadcn slots plus the 7 `status-*` pairs) using the `hsl(var(--x) / <alpha-value>)` pattern, and add `borderRadius` (`lg`/`md`/`sm` derived from `--radius`) and any spacing/animation extensions decided in Steps 1-2.

- [ ] **Step 5: Prove it compiles and renders**

Update `src/web/src/app/page.tsx` temporarily to render a swatch of each new color token (a labeled `div` per token using `bg-<token>`), run `cd src/web && npm run dev`, and visually confirm every token renders a distinct, on-brand color (manual check — no automated visual regression tooling is introduced in this phase per spec §8). Revert `page.tsx` to its original content once confirmed (its real content is decided in Task 10).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/app/globals.css src/web/tailwind.config.ts
git commit -m "feat(web): establish design token system (color, radius, status semantics)"
```

---

### Task 4: Auth BFF — cookie session, Route Handlers, refresh rotation

**Files:**
- Create: `src/web/src/lib/env.ts`
- Create: `src/web/src/services/authService.ts`
- Create: `src/web/src/lib/auth/session.ts`
- Create: `src/web/src/lib/auth/apiFetch.ts`
- Create: `src/web/src/app/api/auth/login/route.ts`
- Create: `src/web/src/app/api/auth/logout/route.ts`
- Create: `src/web/src/app/api/auth/session/route.ts`
- Test: `src/web/src/lib/auth/session.test.ts`

**Interfaces:**
- Produces: `getUpstreamApiUrl(): string`; `authService.login(email, password): Promise<{accessToken, refreshToken, userId, organizationId}>`, `authService.refresh(refreshToken): Promise<...same shape>`, `authService.me(accessToken): Promise<MeResponse>` (mirrors Task 1's backend shape exactly — `permissions: string[]`, `hasAllBranchesScope: boolean`, `branches: {branchId, branchName, expiresAt}[]`); `setSessionCookies(accessToken, refreshToken)`, `clearSessionCookies()`, `getValidAccessToken(): Promise<string | null>` (reads cookies, transparently refreshes and re-sets cookies if the access token is expired, returns `null` if the refresh token is also invalid); `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/session` (returns `MeResponse | null`, `401` body `null` — never the raw tokens). Task 5's `useSession` hook consumes `GET /api/auth/session` only.

- [ ] **Step 1: Env helper**

Create `src/web/src/lib/env.ts`:

```typescript
export function getUpstreamApiUrl(): string {
  const url = process.env.NEXT_PUBLIC_API_BASE_URL;
  if (!url) {
    throw new Error("NEXT_PUBLIC_API_BASE_URL is not set");
  }
  return url;
}
```

(Reused server-side only, from Route Handlers — client components must never call the upstream API URL directly; they call same-origin `/api/*` routes.)

- [ ] **Step 2: Backend-facing auth service**

Create `src/web/src/services/authService.ts`:

```typescript
import { getUpstreamApiUrl } from "@/lib/env";

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  userId: string;
  organizationId: string;
}

export interface MeBranchGrant {
  branchId: string;
  branchName: string;
  expiresAt: string | null;
}

export interface MeResponse {
  userId: string;
  organizationId: string;
  email: string;
  fullName: string;
  permissions: string[];
  hasAllBranchesScope: boolean;
  branches: MeBranchGrant[];
}

export const authService = {
  async login(email: string, password: string): Promise<AuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("LOGIN_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      userId: data.userId,
      organizationId: data.organizationId,
    };
  },

  async refresh(refreshToken: string): Promise<AuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("REFRESH_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      userId: data.userId,
      organizationId: data.organizationId,
    };
  },

  async me(accessToken: string): Promise<MeResponse> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/me`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("ME_FAILED");
    }
    return response.json();
  },
};
```

- [ ] **Step 3: Cookie session helpers**

Create `src/web/src/lib/auth/session.ts`:

```typescript
import { cookies } from "next/headers";
import { authService } from "@/services/authService";

const ACCESS_COOKIE = "courier_access_token";
const REFRESH_COOKIE = "courier_refresh_token";

const cookieOptions = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/",
};

export function setSessionCookies(accessToken: string, refreshToken: string) {
  const store = cookies();
  store.set(ACCESS_COOKIE, accessToken, { ...cookieOptions, maxAge: 60 * 15 });
  store.set(REFRESH_COOKIE, refreshToken, { ...cookieOptions, maxAge: 60 * 60 * 24 * 7 });
}

export function clearSessionCookies() {
  const store = cookies();
  store.delete(ACCESS_COOKIE);
  store.delete(REFRESH_COOKIE);
}

// Returns a usable access token, transparently refreshing (and re-setting cookies) if the
// stored access token has expired. Returns null only when there is no session at all, or the
// refresh token itself is invalid/expired/revoked — callers treat null as "not logged in".
export async function getValidAccessToken(): Promise<string | null> {
  const store = cookies();
  const accessToken = store.get(ACCESS_COOKIE)?.value;
  const refreshToken = store.get(REFRESH_COOKIE)?.value;

  if (accessToken) {
    return accessToken;
  }
  if (!refreshToken) {
    return null;
  }

  try {
    const tokens = await authService.refresh(refreshToken);
    setSessionCookies(tokens.accessToken, tokens.refreshToken);
    return tokens.accessToken;
  } catch {
    clearSessionCookies();
    return null;
  }
}
```

(The access-token cookie's own 15-minute `maxAge` — matching the backend's `JwtOptions.AccessTokenMinutes` — means the browser stops sending it once expired, so `getValidAccessToken` naturally falls into the refresh branch without needing to decode the JWT to check expiry.)

- [ ] **Step 4: Authenticated fetch wrapper**

Create `src/web/src/lib/auth/apiFetch.ts`:

```typescript
import { getUpstreamApiUrl } from "@/lib/env";
import { getValidAccessToken } from "@/lib/auth/session";

// Server-only helper for later feature phases (F1+) to call the real API with the current
// session's bearer token attached, refreshing transparently via getValidAccessToken.
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const accessToken = await getValidAccessToken();
  if (!accessToken) {
    throw new Error("NOT_AUTHENTICATED");
  }
  return fetch(`${getUpstreamApiUrl()}${path}`, {
    ...init,
    headers: {
      ...init.headers,
      Authorization: `Bearer ${accessToken}`,
    },
    cache: "no-store",
  });
}
```

- [ ] **Step 5: Login/logout/session Route Handlers**

Create `src/web/src/app/api/auth/login/route.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";
import { authService } from "@/services/authService";
import { setSessionCookies } from "@/lib/auth/session";

export async function POST(request: NextRequest) {
  const { email, password } = await request.json();
  try {
    const tokens = await authService.login(email, password);
    setSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }
}
```

Create `src/web/src/app/api/auth/logout/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { clearSessionCookies } from "@/lib/auth/session";

export async function POST() {
  clearSessionCookies();
  return NextResponse.json({ ok: true });
}
```

Create `src/web/src/app/api/auth/session/route.ts`:

```typescript
import { NextResponse } from "next/server";
import { getValidAccessToken } from "@/lib/auth/session";
import { authService } from "@/services/authService";

export async function GET() {
  const accessToken = await getValidAccessToken();
  if (!accessToken) {
    return NextResponse.json(null, { status: 401 });
  }
  try {
    const me = await authService.me(accessToken);
    return NextResponse.json(me);
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
```

- [ ] **Step 6: Write a unit test for the cookie helpers**

Create `src/web/src/lib/auth/session.test.ts`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";

const cookieStore = new Map<string, string>();

vi.mock("next/headers", () => ({
  cookies: () => ({
    get: (name: string) => (cookieStore.has(name) ? { value: cookieStore.get(name) } : undefined),
    set: (name: string, value: string) => {
      cookieStore.set(name, value);
    },
    delete: (name: string) => {
      cookieStore.delete(name);
    },
  }),
}));

import { setSessionCookies, clearSessionCookies, getValidAccessToken } from "./session";

describe("session cookies", () => {
  beforeEach(() => {
    cookieStore.clear();
  });

  it("returns the stored access token when present", async () => {
    setSessionCookies("access-123", "refresh-456");

    const token = await getValidAccessToken();

    expect(token).toBe("access-123");
  });

  it("returns null when there is no session at all", async () => {
    const token = await getValidAccessToken();

    expect(token).toBeNull();
  });

  it("clears both cookies on logout", async () => {
    setSessionCookies("access-123", "refresh-456");

    clearSessionCookies();

    expect(await getValidAccessToken()).toBeNull();
  });
});
```

- [ ] **Step 7: Run the test**

Run: `cd src/web && npm test -- session.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 8: Commit**

```bash
git add src/web/src/lib/env.ts src/web/src/services/authService.ts src/web/src/lib/auth/session.ts src/web/src/lib/auth/session.test.ts src/web/src/lib/auth/apiFetch.ts src/web/src/app/api/auth
git commit -m "feat(web): add auth BFF (httpOnly cookie session, login/logout/session routes)"
```

---

### Task 5: `useSession` + `usePermission` hooks

**Files:**
- Create: `src/web/src/features/auth/hooks/useSession.ts`
- Create: `src/web/src/features/auth/hooks/usePermission.ts`
- Test: `src/web/src/features/auth/hooks/usePermission.test.tsx`

**Interfaces:**
- Consumes: `GET /api/auth/session` (Task 4), returning `MeResponse | null`.
- Produces: `useSession(): UseQueryResult<MeResponse | null>`; `usePermission(permission: string, options?: { branchId?: string }): boolean`. Every later phase's nav items, action buttons, and route guards call `usePermission` — no other permission-check mechanism is introduced.

- [ ] **Step 1: `useSession`**

Create `src/web/src/features/auth/hooks/useSession.ts`:

```typescript
"use client";

import { useQuery } from "@tanstack/react-query";
import type { MeResponse } from "@/services/authService";

async function fetchSession(): Promise<MeResponse | null> {
  const response = await fetch("/api/auth/session");
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error("SESSION_FETCH_FAILED");
  }
  return response.json();
}

export function useSession() {
  return useQuery({
    queryKey: ["session"],
    queryFn: fetchSession,
    staleTime: 60_000,
    retry: false,
  });
}
```

- [ ] **Step 2: `usePermission`**

Create `src/web/src/features/auth/hooks/usePermission.ts`:

```typescript
"use client";

import { useSession } from "@/features/auth/hooks/useSession";

// A branch-scoped check only ever narrows access further — it never grants a permission the
// user's overall permission set doesn't already contain. Branch-level enforcement of *who can
// act on which branch's data* still happens server-side on every request (CLAUDE.md §33); this
// hook only controls what the UI shows, matching hasAllBranchesScope/branches from /auth/me.
export function usePermission(permission: string, options?: { branchId?: string }): boolean {
  const { data: session } = useSession();
  if (!session) {
    return false;
  }
  if (!session.permissions.includes(permission)) {
    return false;
  }
  if (!options?.branchId) {
    return true;
  }
  if (session.hasAllBranchesScope) {
    return true;
  }
  return session.branches.some((b) => b.branchId === options.branchId);
}
```

- [ ] **Step 3: Write the test**

Create `src/web/src/features/auth/hooks/usePermission.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { usePermission } from "./usePermission";

function TestComponent({ permission }: { permission: string }) {
  const allowed = usePermission(permission);
  return <div>{allowed ? "allowed" : "denied"}</div>;
}

function renderWithSession(session: unknown) {
  const originalFetch = global.fetch;
  global.fetch = vi.fn().mockResolvedValue({
    ok: session !== null,
    status: session === null ? 401 : 200,
    json: async () => session,
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <TestComponent permission="shipment.create" />
    </QueryClientProvider>
  );
  return { ...result, restore: () => (global.fetch = originalFetch) };
}

describe("usePermission", () => {
  it("returns denied while there is no session", async () => {
    const { restore } = renderWithSession(null);
    await waitFor(() => screen.getByText("denied"));
    restore();
  });

  it("returns allowed when the permission is in the session's permission list", async () => {
    const { restore } = renderWithSession({
      userId: "u1",
      organizationId: "o1",
      email: "a@b.com",
      fullName: "A B",
      permissions: ["shipment.create"],
      hasAllBranchesScope: true,
      branches: [],
    });
    await waitFor(() => screen.getByText("allowed"));
    restore();
  });
});
```

- [ ] **Step 4: Run the test**

Run: `cd src/web && npm test -- usePermission.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/auth/hooks
git commit -m "feat(web): add useSession and usePermission hooks"
```

---

### Task 6: Login screen

**Files:**
- Create (via `npx shadcn@latest add button input label card`): `src/web/src/components/ui/button.tsx`, `src/web/src/components/ui/input.tsx`, `src/web/src/components/ui/label.tsx`, `src/web/src/components/ui/card.tsx`
- Create: `src/web/src/features/auth/schema.ts`
- Create: `src/web/src/features/auth/LoginForm.tsx`
- Create: `src/web/src/app/login/page.tsx`
- Test: `src/web/src/features/auth/LoginForm.test.tsx`

**Interfaces:**
- Consumes: `POST /api/auth/login` (Task 4), design tokens (Task 3, via Tailwind classes only — no raw colors).
- Produces: `LoginForm` component, mountable standalone; `app/login` route.

- [ ] **Step 1: Add shadcn primitives**

```bash
cd src/web
npx shadcn@latest add button input label card --yes
```

Expected: `src/web/src/components/ui/{button,input,label,card}.tsx` are created. If prompted interactively despite `--yes`, accept the `components.json` values written in Task 2 for every prompt.

- [ ] **Step 2: Zod schema**

Create `src/web/src/features/auth/schema.ts`:

```typescript
import { z } from "zod";

export const loginSchema = z.object({
  email: z.string().email("Enter a valid email address."),
  password: z.string().min(1, "Password is required."),
});

export type LoginFormValues = z.infer<typeof loginSchema>;
```

- [ ] **Step 3: `LoginForm`**

Create `src/web/src/features/auth/LoginForm.tsx`:

```typescript
"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { loginSchema, type LoginFormValues } from "@/features/auth/schema";

export function LoginForm() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) });

  async function onSubmit(values: LoginFormValues) {
    setServerError(null);
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setServerError(body.error ?? "Login failed.");
      return;
    }
    router.push("/dashboard");
  }

  return (
    <Card className="w-full max-w-sm">
      <CardHeader>
        <CardTitle>Sign in</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <Input id="email" type="email" autoComplete="email" {...register("email")} />
            {errors.email && (
              <p className="text-sm text-destructive">{errors.email.message}</p>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input id="password" type="password" autoComplete="current-password" {...register("password")} />
            {errors.password && (
              <p className="text-sm text-destructive">{errors.password.message}</p>
            )}
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Signing in..." : "Sign in"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
```

Run `cd src/web && npm install @hookform/resolvers` if not already a dependency (it isn't — check `package.json` first).

- [ ] **Step 4: Login page**

Create `src/web/src/app/login/page.tsx`:

```typescript
import { LoginForm } from "@/features/auth/LoginForm";

export default function LoginPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-4">
      <LoginForm />
    </main>
  );
}
```

- [ ] **Step 5: Write the test**

Create `src/web/src/features/auth/LoginForm.test.tsx`:

```typescript
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { LoginForm } from "./LoginForm";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("LoginForm", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("shows validation errors for an empty submit", async () => {
    render(<LoginForm />);

    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/enter a valid email/i)).toBeInTheDocument();
  });

  it("shows a server error on failed login", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      json: async () => ({ error: "Invalid email or password." }),
    });
    render(<LoginForm />);

    fireEvent.change(screen.getByLabelText(/email/i), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText(/password/i), { target: { value: "wrong" } });
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText("Invalid email or password.")).toBeInTheDocument());
  });
});
```

- [ ] **Step 6: Run the tests**

Run: `cd src/web && npm test -- LoginForm.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/web/src/components/ui/button.tsx src/web/src/components/ui/input.tsx src/web/src/components/ui/label.tsx src/web/src/components/ui/card.tsx src/web/src/features/auth/schema.ts src/web/src/features/auth/LoginForm.tsx src/web/src/features/auth/LoginForm.test.tsx src/web/src/app/login src/web/package.json src/web/package-lock.json
git commit -m "feat(web): add login screen"
```

---

### Task 7: App shell — nav, sidebar, branch switcher, route protection

**Files:**
- Create (via `npx shadcn@latest add avatar dropdown-menu separator sheet`): `src/web/src/components/ui/{avatar,dropdown-menu,separator,sheet}.tsx`
- Create: `src/web/src/lib/store/uiStore.ts`
- Create: `src/web/src/components/layout/TopNav.tsx`
- Create: `src/web/src/components/layout/Sidebar.tsx`
- Create: `src/web/src/components/layout/BranchSwitcher.tsx`
- Create: `src/web/src/components/layout/AppShell.tsx`
- Create: `src/web/src/app/(staff)/layout.tsx`
- Create: `src/web/src/middleware.ts`
- Test: `src/web/src/components/layout/Sidebar.test.tsx`

**Interfaces:**
- Consumes: `useSession`/`usePermission` (Task 5), design tokens (Task 3).
- Produces: `<AppShell>{children}</AppShell>` layout wrapper used by every `(staff)` route from Task 10 onward; `useUiStore()` Zustand store with `activeBranchId: string | null`, `setActiveBranchId(id: string | null): void`, `sidebarCollapsed: boolean`, `toggleSidebar(): void`.

- [ ] **Step 1: Add shadcn primitives**

```bash
cd src/web
npx shadcn@latest add avatar dropdown-menu separator sheet --yes
```

- [ ] **Step 2: UI store**

Create `src/web/src/lib/store/uiStore.ts`:

```typescript
import { create } from "zustand";

interface UiState {
  activeBranchId: string | null;
  setActiveBranchId: (id: string | null) => void;
  sidebarCollapsed: boolean;
  toggleSidebar: () => void;
}

export const useUiStore = create<UiState>((set) => ({
  activeBranchId: null,
  setActiveBranchId: (id) => set({ activeBranchId: id }),
  sidebarCollapsed: false,
  toggleSidebar: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),
}));
```

- [ ] **Step 3: Nav item catalog and `Sidebar`**

Create `src/web/src/components/layout/Sidebar.tsx`:

```typescript
"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useUiStore } from "@/lib/store/uiStore";

interface NavItem {
  href: string;
  label: string;
  permission: string;
}

// The full staff nav catalog. Phases F1-F9 each add their real route once built; entries below
// point at routes that don't exist until their phase lands, which is fine — Link just 404s
// until then, and the item is hidden entirely for any user lacking the permission regardless.
const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", label: "Dashboard", permission: "report.read" },
  { href: "/shipments", label: "Shipments", permission: "shipment.read" },
  { href: "/branches", label: "Branches", permission: "branch.read" },
  { href: "/cod", label: "COD", permission: "cod.read" },
];

export function Sidebar() {
  const pathname = usePathname();
  const collapsed = useUiStore((s) => s.sidebarCollapsed);

  return (
    <nav
      className={cn(
        "flex flex-col gap-1 border-r border-border bg-card p-2 transition-all",
        collapsed ? "w-16" : "w-56"
      )}
      aria-label="Primary"
    >
      {NAV_ITEMS.map((item) => (
        <SidebarLink key={item.href} item={item} active={pathname === item.href} collapsed={collapsed} />
      ))}
    </nav>
  );
}

function SidebarLink({ item, active, collapsed }: { item: NavItem; active: boolean; collapsed: boolean }) {
  const allowed = usePermission(item.permission);
  if (!allowed) {
    return null;
  }
  return (
    <Link
      href={item.href}
      className={cn(
        "rounded-md px-3 py-2 text-sm font-medium text-foreground hover:bg-accent",
        active && "bg-accent",
        collapsed && "text-center"
      )}
    >
      {collapsed ? item.label[0] : item.label}
    </Link>
  );
}
```

- [ ] **Step 4: `BranchSwitcher`**

Create `src/web/src/components/layout/BranchSwitcher.tsx`:

```typescript
"use client";

import { useEffect } from "react";
import { useSession } from "@/features/auth/hooks/useSession";
import { useUiStore } from "@/lib/store/uiStore";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Button } from "@/components/ui/button";

// Renders nothing for a single-branch user (design spec §F2 exit criteria) — a switcher
// implies a choice, and a user with exactly one branch (or none) has none to make.
export function BranchSwitcher() {
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

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm">
          {active?.branchName ?? "Select branch"}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent>
        {session.branches.map((branch) => (
          <DropdownMenuItem key={branch.branchId} onSelect={() => setActiveBranchId(branch.branchId)}>
            {branch.branchName}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

- [ ] **Step 5: `TopNav`**

Create `src/web/src/components/layout/TopNav.tsx`:

```typescript
"use client";

import { useRouter } from "next/navigation";
import { useSession } from "@/features/auth/hooks/useSession";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

function initials(fullName: string): string {
  return fullName
    .split(" ")
    .map((part) => part[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export function TopNav() {
  const router = useRouter();
  const { data: session } = useSession();

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
  }

  return (
    <header className="flex h-14 items-center justify-between border-b border-border bg-card px-4">
      <span className="font-semibold text-foreground">Courier Platform</span>
      <div className="flex items-center gap-4">
        <BranchSwitcher />
        {session && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button aria-label="User menu">
                <Avatar>
                  <AvatarFallback>{initials(session.fullName)}</AvatarFallback>
                </Avatar>
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onSelect={handleLogout}>Log out</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </header>
  );
}
```

- [ ] **Step 6: `AppShell`**

Create `src/web/src/components/layout/AppShell.tsx`:

```typescript
import type { ReactNode } from "react";
import { TopNav } from "@/components/layout/TopNav";
import { Sidebar } from "@/components/layout/Sidebar";

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex h-screen flex-col">
      <TopNav />
      <div className="flex flex-1 overflow-hidden">
        <Sidebar />
        <main className="flex-1 overflow-y-auto bg-background p-6">{children}</main>
      </div>
    </div>
  );
}
```

- [ ] **Step 7: `(staff)` layout and middleware route protection**

Create `src/web/src/app/(staff)/layout.tsx`:

```typescript
import type { ReactNode } from "react";
import { AppShell } from "@/components/layout/AppShell";

export default function StaffLayout({ children }: { children: ReactNode }) {
  return <AppShell>{children}</AppShell>;
}
```

Create `src/web/src/middleware.ts`:

```typescript
import { NextRequest, NextResponse } from "next/server";

// UX-level redirect only — presence of a cookie is not proof of a valid session. The real
// authorization boundary is the backend API on every request (CLAUDE.md §33); if the access
// token cookie is stale and the refresh token is also invalid, /api/auth/session's 401 (Task 4)
// is what actually enforces the redirect the client-side code follows.
export function middleware(request: NextRequest) {
  const hasSession =
    request.cookies.has("courier_access_token") || request.cookies.has("courier_refresh_token");
  if (!hasSession) {
    const loginUrl = new URL("/login", request.url);
    return NextResponse.redirect(loginUrl);
  }
  return NextResponse.next();
}

export const config = {
  matcher: ["/dashboard/:path*", "/shipments/:path*", "/branches/:path*", "/cod/:path*"],
};
```

- [ ] **Step 8: Write the test**

Create `src/web/src/components/layout/Sidebar.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Sidebar } from "./Sidebar";

vi.mock("next/navigation", () => ({
  usePathname: () => "/dashboard",
}));

function renderSidebar(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u1",
      organizationId: "o1",
      email: "a@b.com",
      fullName: "A B",
      permissions,
      hasAllBranchesScope: true,
      branches: [],
    }),
  }) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <Sidebar />
    </QueryClientProvider>
  );
}

describe("Sidebar", () => {
  it("hides nav items the session lacks permission for", async () => {
    renderSidebar(["report.read"]);

    await waitFor(() => expect(screen.getByText("Dashboard")).toBeInTheDocument());
    expect(screen.queryByText("Shipments")).not.toBeInTheDocument();
  });

  it("shows nav items granted by the session's permissions", async () => {
    renderSidebar(["report.read", "shipment.read"]);

    await waitFor(() => expect(screen.getByText("Shipments")).toBeInTheDocument());
  });
});
```

- [ ] **Step 9: Run the tests**

Run: `cd src/web && npm test -- Sidebar.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 10: Commit**

```bash
git add src/web/src/components/ui/avatar.tsx src/web/src/components/ui/dropdown-menu.tsx src/web/src/components/ui/separator.tsx src/web/src/components/ui/sheet.tsx src/web/src/lib/store src/web/src/components/layout src/web/src/app/\(staff\) src/web/src/middleware.ts
git commit -m "feat(web): add app shell (top nav, permission-gated sidebar, branch switcher)"
```

---

### Task 8: Shared state primitives — `StatusBadge`, `EmptyState`, `ErrorState`, `LoadingState`

**Files:**
- Create (via `npx shadcn@latest add skeleton badge`): `src/web/src/components/ui/{skeleton,badge}.tsx`
- Create: `src/web/src/components/status-badge/statusConfig.ts`
- Create: `src/web/src/components/status-badge/StatusBadge.tsx`
- Create: `src/web/src/components/states/EmptyState.tsx`
- Create: `src/web/src/components/states/ErrorState.tsx`
- Create: `src/web/src/components/states/LoadingState.tsx`
- Test: `src/web/src/components/status-badge/StatusBadge.test.tsx`

**Interfaces:**
- Consumes: the `--status-*` tokens from Task 3.
- Produces: `<StatusBadge status="IN_TRANSIT" />`; `<EmptyState title description icon? action? />`; `<ErrorState title description retry?() />`; `<LoadingState rows?=number />`. Every later phase (F3 shipment list, F6 NDR queue, F7 COD tables, etc.) uses these instead of ad hoc spinners/blank divs (spec §7).

- [ ] **Step 1: Add shadcn primitives**

```bash
cd src/web
npx shadcn@latest add skeleton badge --yes
```

- [ ] **Step 2: Status config — single source of truth**

Create `src/web/src/components/status-badge/statusConfig.ts`. The status list matches `SHIPMENT-LIFECYCLE.md`'s CREATED..DELIVERED happy path plus its failure/return branches, mapped to the closest semantic token from Task 3:

```typescript
export interface StatusConfigEntry {
  label: string;
  colorClass: string;
}

export const STATUS_CONFIG: Record<string, StatusConfigEntry> = {
  CREATED: { label: "Created", colorClass: "bg-status-created text-status-created-foreground" },
  PICKUP_ASSIGNED: { label: "Pickup Assigned", colorClass: "bg-status-created text-status-created-foreground" },
  PICKED_UP: { label: "Picked Up", colorClass: "bg-status-in-transit text-status-in-transit-foreground" },
  AT_ORIGIN_BRANCH: { label: "At Origin Branch", colorClass: "bg-status-in-transit text-status-in-transit-foreground" },
  SORTED: { label: "Sorted", colorClass: "bg-status-in-transit text-status-in-transit-foreground" },
  IN_TRANSIT: { label: "In Transit", colorClass: "bg-status-in-transit text-status-in-transit-foreground" },
  AT_DESTINATION_BRANCH: { label: "At Destination Branch", colorClass: "bg-status-in-transit text-status-in-transit-foreground" },
  OUT_FOR_DELIVERY: { label: "Out for Delivery", colorClass: "bg-status-out-for-delivery text-status-out-for-delivery-foreground" },
  DELIVERED: { label: "Delivered", colorClass: "bg-status-delivered text-status-delivered-foreground" },
  DELIVERY_FAILED: { label: "Delivery Failed", colorClass: "bg-status-delivery-failed text-status-delivery-failed-foreground" },
  NDR_CREATED: { label: "NDR", colorClass: "bg-status-ndr text-status-ndr-foreground" },
  REATTEMPT: { label: "Reattempt", colorClass: "bg-status-out-for-delivery text-status-out-for-delivery-foreground" },
  RTO_INITIATED: { label: "RTO Initiated", colorClass: "bg-status-rto text-status-rto-foreground" },
  RTO_IN_TRANSIT: { label: "RTO In Transit", colorClass: "bg-status-rto text-status-rto-foreground" },
  RTO_AT_ORIGIN: { label: "RTO At Origin", colorClass: "bg-status-rto text-status-rto-foreground" },
  RTO_DELIVERED: { label: "RTO Delivered", colorClass: "bg-status-rto text-status-rto-foreground" },
  CANCELLED: { label: "Cancelled", colorClass: "bg-status-cancelled text-status-cancelled-foreground" },
};

export function resolveStatusConfig(status: string): StatusConfigEntry {
  return STATUS_CONFIG[status] ?? { label: status, colorClass: "bg-muted text-muted-foreground" };
}
```

- [ ] **Step 3: `StatusBadge`**

Create `src/web/src/components/status-badge/StatusBadge.tsx`:

```typescript
import { Badge } from "@/components/ui/badge";
import { resolveStatusConfig } from "@/components/status-badge/statusConfig";

export function StatusBadge({ status }: { status: string }) {
  const config = resolveStatusConfig(status);
  return <Badge className={config.colorClass}>{config.label}</Badge>;
}
```

- [ ] **Step 4: `EmptyState`, `ErrorState`, `LoadingState`**

Create `src/web/src/components/states/EmptyState.tsx`:

```typescript
import type { ReactNode } from "react";

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-lg border border-dashed border-border p-12 text-center">
      <p className="text-sm font-medium text-foreground">{title}</p>
      {description && <p className="text-sm text-muted-foreground">{description}</p>}
      {action}
    </div>
  );
}
```

Create `src/web/src/components/states/ErrorState.tsx`:

```typescript
import { Button } from "@/components/ui/button";

export function ErrorState({
  title = "Something went wrong",
  description,
  onRetry,
}: {
  title?: string;
  description?: string;
  onRetry?: () => void;
}) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 rounded-lg border border-destructive/30 bg-destructive/5 p-12 text-center">
      <p className="text-sm font-medium text-destructive">{title}</p>
      {description && <p className="text-sm text-muted-foreground">{description}</p>}
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  );
}
```

Create `src/web/src/components/states/LoadingState.tsx`:

```typescript
import { Skeleton } from "@/components/ui/skeleton";

export function LoadingState({ rows = 5 }: { rows?: number }) {
  return (
    <div className="space-y-2">
      {Array.from({ length: rows }).map((_, i) => (
        <Skeleton key={i} className="h-10 w-full" />
      ))}
    </div>
  );
}
```

- [ ] **Step 5: Write the test**

Create `src/web/src/components/status-badge/StatusBadge.test.tsx`:

```typescript
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusBadge } from "./StatusBadge";

describe("StatusBadge", () => {
  it("renders the mapped label for a known status", () => {
    render(<StatusBadge status="IN_TRANSIT" />);
    expect(screen.getByText("In Transit")).toBeInTheDocument();
  });

  it("falls back to the raw status string for an unknown status", () => {
    render(<StatusBadge status="SOME_FUTURE_STATUS" />);
    expect(screen.getByText("SOME_FUTURE_STATUS")).toBeInTheDocument();
  });
});
```

- [ ] **Step 6: Run the tests**

Run: `cd src/web && npm test -- StatusBadge.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/web/src/components/ui/skeleton.tsx src/web/src/components/ui/badge.tsx src/web/src/components/status-badge src/web/src/components/states
git commit -m "feat(web): add StatusBadge and empty/error/loading state primitives"
```

---

### Task 9: `DataTable` pattern (server-side pagination)

**Files:**
- Create (via `npx shadcn@latest add table`): `src/web/src/components/ui/table.tsx`
- Create: `src/web/src/components/data-table/DataTable.tsx`
- Test: `src/web/src/components/data-table/DataTable.test.tsx`

**Interfaces:**
- Consumes: the backend's `PagedResult<T>` shape (`src/SharedKernel/PagedResult.cs`: `{ items: T[], page: number, pageSize: number, totalCount: number, totalPages: number }` — camelCase over the wire per ASP.NET Core's default JSON casing), `EmptyState`/`LoadingState` (Task 8).
- Produces: `<DataTable columns={ColumnDef<T>[]} data={PagedResult<T>} isLoading onPageChange={(page: number) => void} />` — generic, reused by every later phase's list view (shipments, NDR queue, COD collections, etc.), so no feature reimplements pagination controls.

- [ ] **Step 1: Add the shadcn table primitive**

```bash
cd src/web
npx shadcn@latest add table --yes
```

- [ ] **Step 2: `DataTable`**

Create `src/web/src/components/data-table/DataTable.tsx`:

```typescript
import type { ReactNode } from "react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/states/EmptyState";
import { LoadingState } from "@/components/states/LoadingState";

export interface ColumnDef<T> {
  key: string;
  header: string;
  render: (row: T) => ReactNode;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface DataTableProps<T> {
  columns: ColumnDef<T>[];
  data?: PagedResult<T>;
  isLoading: boolean;
  emptyTitle: string;
  emptyDescription?: string;
  onPageChange: (page: number) => void;
  rowKey: (row: T) => string;
}

export function DataTable<T>({
  columns,
  data,
  isLoading,
  emptyTitle,
  emptyDescription,
  onPageChange,
  rowKey,
}: DataTableProps<T>) {
  if (isLoading) {
    return <LoadingState rows={data?.pageSize ?? 5} />;
  }
  if (!data || data.items.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <div className="space-y-4">
      <Table>
        <TableHeader>
          <TableRow>
            {columns.map((col) => (
              <TableHead key={col.key}>{col.header}</TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {data.items.map((row) => (
            <TableRow key={rowKey(row)}>
              {columns.map((col) => (
                <TableCell key={col.key}>{col.render(row)}</TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          Page {data.page} of {data.totalPages} ({data.totalCount} total)
        </p>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={data.page <= 1}
            onClick={() => onPageChange(data.page - 1)}
          >
            Previous
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={data.page >= data.totalPages}
            onClick={() => onPageChange(data.page + 1)}
          >
            Next
          </Button>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Write the test**

Create `src/web/src/components/data-table/DataTable.test.tsx`:

```typescript
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { DataTable, type ColumnDef } from "./DataTable";

interface Row {
  id: string;
  name: string;
}

const columns: ColumnDef<Row>[] = [{ key: "name", header: "Name", render: (row) => row.name }];

describe("DataTable", () => {
  it("shows the empty state when there are no items", () => {
    render(
      <DataTable
        columns={columns}
        data={{ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }}
        isLoading={false}
        emptyTitle="No rows"
        onPageChange={vi.fn()}
        rowKey={(r) => r.id}
      />
    );
    expect(screen.getByText("No rows")).toBeInTheDocument();
  });

  it("renders rows and calls onPageChange from Next", () => {
    const onPageChange = vi.fn();
    render(
      <DataTable
        columns={columns}
        data={{
          items: [{ id: "1", name: "Row One" }],
          page: 1,
          pageSize: 10,
          totalCount: 20,
          totalPages: 2,
        }}
        isLoading={false}
        emptyTitle="No rows"
        onPageChange={onPageChange}
        rowKey={(r) => r.id}
      />
    );

    expect(screen.getByText("Row One")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /next/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });
});
```

- [ ] **Step 4: Run the tests**

Run: `cd src/web && npm test -- DataTable.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/components/ui/table.tsx src/web/src/components/data-table
git commit -m "feat(web): add DataTable pattern with server-side pagination controls"
```

---

### Task 10: Wire it together — root providers, dashboard placeholder, exit-criteria proof

**Files:**
- Modify: `src/web/src/app/providers.tsx`
- Modify: `src/web/src/app/page.tsx`
- Modify: `src/web/src/app/page.test.tsx`
- Create: `src/web/src/app/(staff)/dashboard/page.tsx`
- Delete: (none)
- Modify: `docs/FRONTEND-ROADMAP.md`

**Interfaces:**
- Consumes: everything from Tasks 2-9.
- Produces: the running app itself — this task's exit criteria is the phase's exit criteria.

- [ ] **Step 1: Redirect the root page to `/dashboard` (or `/login`)**

Replace `src/web/src/app/page.tsx`:

```typescript
import { redirect } from "next/navigation";
import { getValidAccessToken } from "@/lib/auth/session";

export default async function RootPage() {
  const accessToken = await getValidAccessToken();
  redirect(accessToken ? "/dashboard" : "/login");
}
```

- [ ] **Step 2: Update the now-invalid root page test**

Replace `src/web/src/app/page.test.tsx` — the old test rendered static heading markup that no longer exists; `RootPage` is now a server component that redirects, which is exercised by the middleware/E2E path, not a unit render. Delete the file's content and replace with a placeholder-free note test that documents the redirect behavior at the unit level using `next/navigation`'s `redirect` mock:

```typescript
import { describe, expect, it, vi } from "vitest";

const redirectMock = vi.fn();
vi.mock("next/navigation", () => ({ redirect: redirectMock }));
vi.mock("@/lib/auth/session", () => ({
  getValidAccessToken: vi.fn(),
}));

import RootPage from "./page";
import { getValidAccessToken } from "@/lib/auth/session";

describe("RootPage", () => {
  it("redirects to /dashboard when a session exists", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue("token-123");

    await RootPage();

    expect(redirectMock).toHaveBeenCalledWith("/dashboard");
  });

  it("redirects to /login when there is no session", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue(null);

    await RootPage();

    expect(redirectMock).toHaveBeenCalledWith("/login");
  });
});
```

- [ ] **Step 3: Dashboard placeholder**

Create `src/web/src/app/(staff)/dashboard/page.tsx`:

```typescript
import { EmptyState } from "@/components/states/EmptyState";

export default function DashboardPage() {
  return (
    <EmptyState
      title="Dashboard coming in Phase F1"
      description="Organization-wide shipment, NDR/RTO, and COD summary tiles land in Frontend Phase F1."
    />
  );
}
```

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd src/web && npm test`
Expected: PASS — every test added across Tasks 4-10.

- [ ] **Step 5: Run the frontend build**

Run: `cd src/web && npm run build`
Expected: build succeeds with no type errors.

- [ ] **Step 6: Manual end-to-end verification**

Start Postgres/API per `docker-compose.yml` (`docker compose up postgres api`) and the frontend dev server (`cd src/web && npm run dev`). In a browser: visit `http://localhost:3000` — confirm redirect to `/login`; log in with a registered org owner (register one first via `POST /api/v1/auth/login`'s companion `/register` endpoint, or an existing dev account) — confirm redirect to `/dashboard` inside the themed app shell, with the sidebar showing only nav items the account's permissions grant, and the `EmptyState` placeholder rendering. This is the concrete proof of `docs/FRONTEND-ROADMAP.md`'s Phase F0 exit criteria — no automated E2E tool is introduced this phase (spec §8), so this manual pass is the recorded verification.

- [ ] **Step 7: Update the roadmap status**

In `docs/FRONTEND-ROADMAP.md`, replace Phase F0's `*Status: Not started.*` line with a completion note in the same style as `docs/ROADMAP.md`'s per-phase status write-ups (date, what was built, what's deferred to F1). Write this after Step 6's manual verification actually passes, not before.

- [ ] **Step 8: Commit**

```bash
git add src/web/src/app/providers.tsx src/web/src/app/page.tsx src/web/src/app/page.test.tsx src/web/src/app/\(staff\)/dashboard docs/FRONTEND-ROADMAP.md
git commit -m "feat(web): wire dashboard placeholder and root redirect; close out Phase F0"
```

---

## Post-plan note

Task 10 Step 2 removes the last reference to the original `create-next-app` heading markup; nothing else in the skeleton is left unused. Zod's `@hookform/resolvers` package (Task 6) is the one dependency this plan adds beyond what Task 2 lists explicitly — install it when Task 6 is reached, not earlier, since it has no purpose until `LoginForm` exists.
