# Frontend Phase F13 — Public Marketing Landing Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the `/` redirect-only page with a public, animation-heavy marketing landing page that sells the courier SaaS platform, using a distinct dark "Night Dispatch" visual identity separate from the back-office "Ledger Counter" system.

**Architecture:** A new `src/web/src/features/marketing/` feature folder holds one component per page section (Nav, Hero, FeatureGrid, HowItWorks, ProductWalkthrough, Credibility, FinalCta, Footer) plus a shared copy-data module and a shared Framer Motion reveal-variants hook. `src/web/src/app/page.tsx` composes them and computes one `isAuthenticated` boolean server-side (reusing the existing `getValidAccessToken()` helper) instead of redirecting. New dark-theme CSS custom properties are added to `globals.css`'s `:root` (fixed, not `.dark`-toggled — mirroring the existing `--auth-panel` pattern) and wired into `tailwind.config.ts` as a `marketing-*` color group, so nothing in the existing light "Ledger Counter" system is touched.

**Tech Stack:** Next.js 14 App Router, TypeScript, Tailwind CSS, Framer Motion (new dependency), Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-07-frontend-phase-f13-marketing-landing-page-design.md`

## Global Constraints

- Working brand name is **"Courier Platform"** everywhere in this page's copy — no invented name.
- Every CTA links to `/login` (unauthenticated) or `/dashboard` (authenticated) — no contact form, no new backend route.
- No fabricated testimonials, customer logos, or usage statistics anywhere on the page.
- `framer-motion@^13.2.0` is the only new dependency, used exclusively inside `src/web/src/features/marketing/`.
- Every animated section must respect `prefers-reduced-motion: reduce` and render a fully-visible static state when it's set — no exceptions.
- New CSS tokens live in `globals.css`'s `:root` block only (never `.dark`), prefixed `--marketing-*`, and are wired into `tailwind.config.ts`'s `theme.extend.colors` as `marketing-*` utilities — the existing `--background`/`--foreground`/`--primary`/etc. tokens and their light/dark values are never modified.
- `/login`, `/merchant-login`, `/dashboard`, `/track`, and `middleware.ts` route protection are unchanged — this phase only removes the hard redirect that used to live at `/`.
- All file paths below are relative to `D:\courier\src\web\src\` unless given as `D:\courier\src\web\<file>` explicitly.

---

## Verified Design Tokens (computed, not to be re-derived)

WCAG contrast ratios below were computed with the standard relative-luminance formula against the two marketing backgrounds. Use these exact HSL triplets in Task 2 — do not substitute different values.

| Token | HSL | Paired against | Ratio | Floor |
|---|---|---|---|---|
| `--marketing-fg` (`40.0 33.3% 92.9%`, reused from `.dark`'s `--foreground`) | vs `--marketing-bg` | 16.86:1 | 4.5:1 text ✅ |
| `--marketing-muted-fg` (`232.5 9.8% 67.8%`, reused from `.dark`'s `--muted-foreground`) | vs `--marketing-bg` | 8.09:1 | 4.5:1 text ✅ |
| `--marketing-accent` (`236.0 73.0% 60.0%`, reused from `.dark`'s `--primary`) | vs `--marketing-bg` | 3.55:1 | 3:1 non-text/large-text ✅ (do not use as small body text on `--marketing-bg`) |
| `--marketing-accent-foreground` (= `--marketing-fg`) | vs `--marketing-accent` | 4.75:1 | 4.5:1 text ✅ (safe for button label text on an accent fill) |
| `--marketing-border` (`228 15% 44%`) | vs `--marketing-bg` | 3.42:1 | 3:1 non-text ✅ |

Product-walkthrough status dots (Task 9) reuse the app's own existing `.dark` status HSL values directly (not new values), verified against `--marketing-bg`:

| Status | HSL (from `.dark` in `globals.css`) | vs `--marketing-bg` | Floor |
|---|---|---|---|
| Created (`neutral`) | `234.3 13.2% 68.8%` | 8.14:1 | 4.5:1 ✅ |
| In transit / Out for delivery (`motion`) | `38.0 78.9% 57.3%` | 9.38:1 | 4.5:1 ✅ |
| Delivered (`success`) | `169.7 47.9% 47.5%` | 7.53:1 | 4.5:1 ✅ |

---

### Task 1: Add the Framer Motion dependency

**Files:**
- Modify: `D:\courier\src\web\package.json`

**Interfaces:**
- Produces: the `framer-motion` package available to import as `import { motion, useReducedMotion } from "framer-motion"` in every later task.

- [ ] **Step 1: Add the dependency**

Edit `D:\courier\src\web\package.json`'s `dependencies` block (alphabetical order — insert between `"date-fns"` and `"idb"`):

```json
    "date-fns": "^4.4.0",
    "framer-motion": "^13.2.0",
    "idb": "^8.0.3",
```

- [ ] **Step 2: Install**

Run (from `D:\courier\src\web`): `npm install`
Expected: installs cleanly, `framer-motion` appears in `package-lock.json`, no peer-dependency errors (framer-motion 13's peer range is `react ^18.0.0 || ^19.0.0` — this project pins React 18).

- [ ] **Step 3: Commit**

```bash
git add src/web/package.json src/web/package-lock.json
git commit -m "$(cat <<'EOF'
chore(web): add framer-motion for the marketing landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 2: Marketing dark-theme design tokens

**Files:**
- Modify: `D:\courier\src\web\src\app\globals.css`
- Modify: `D:\courier\src\web\tailwind.config.ts`

**Interfaces:**
- Produces: Tailwind utility classes `bg-marketing-bg`, `bg-marketing-bg-elevated`, `text-marketing-fg`, `text-marketing-muted-fg`, `bg-marketing-accent`, `text-marketing-accent` (uses the same `--marketing-accent` CSS var as a text color), `text-marketing-accent-fg`, `border-marketing-border` — consumed by every component task from Task 5 onward.

- [ ] **Step 1: Add the CSS custom properties**

In `D:\courier\src\web\src\app\globals.css`, add a new block **inside** the existing `:root { ... }` selector, immediately after the `--chart-rank-3: 230.0 35.0% 66.0%;` line and before the closing `}` of `:root` (do not add these to `.dark` — they are fixed regardless of the visitor's theme, the same pattern `--auth-panel` already uses):

```css
  /* Frontend Phase F13 — public marketing landing page ("Night Dispatch"). Fixed,
     dark-forward tokens independent of the visitor's app theme preference (same
     reasoning as --auth-panel above: this is a distinct "front door" surface, not
     part of the in-app light/dark system). --marketing-fg and --marketing-muted-fg
     reuse .dark's --foreground/--muted-foreground values directly rather than new
     numbers, since marketing-bg (6% L) is darker than .dark's own --background
     (10.2% L) and those tokens already clear AA there, so contrast only improves.
     --marketing-accent reuses .dark's --primary value for the same reason. Exact
     computed ratios: fg vs bg 16.86:1, muted-fg vs bg 8.09:1, accent vs bg 3.55:1
     (3:1 non-text/large-text floor — do not use --marketing-accent as small body
     text on --marketing-bg), fg vs accent 4.75:1 (safe for button label text),
     border vs bg 3.42:1. See docs/superpowers/plans/2026-09-07-frontend-phase-f13-
     marketing-landing-page.md's "Verified Design Tokens" table for the full
     derivation. */
  --marketing-bg: 228 20% 6%;
  --marketing-bg-elevated: 228 18% 10%;
  --marketing-fg: 40.0 33.3% 92.9%;
  --marketing-muted-fg: 232.5 9.8% 67.8%;
  --marketing-accent: 236.0 73.0% 60.0%;
  --marketing-accent-foreground: 40.0 33.3% 92.9%;
  --marketing-border: 228 15% 44%;
```

- [ ] **Step 2: Wire the tokens into Tailwind**

In `D:\courier\src\web\tailwind.config.ts`, add a new entry inside `theme.extend.colors`, immediately after the `sidebar: { ... }` block (after its closing `},` on line 88, before the closing `},` of `colors` on line 89):

```ts
        "marketing-bg": "hsl(var(--marketing-bg) / <alpha-value>)",
        "marketing-bg-elevated": "hsl(var(--marketing-bg-elevated) / <alpha-value>)",
        "marketing-fg": "hsl(var(--marketing-fg) / <alpha-value>)",
        "marketing-muted-fg": "hsl(var(--marketing-muted-fg) / <alpha-value>)",
        "marketing-accent": "hsl(var(--marketing-accent) / <alpha-value>)",
        "marketing-accent-fg": "hsl(var(--marketing-accent-foreground) / <alpha-value>)",
        "marketing-border": "hsl(var(--marketing-border) / <alpha-value>)",
```

- [ ] **Step 3: Type-check**

Run (from `D:\courier\src\web`): `npx tsc --noEmit`
Expected: no errors (confirms `tailwind.config.ts` is still valid TypeScript). Functional verification that the utilities actually generate correct CSS happens once a component consumes them, starting Task 5, and again in Task 14's full production build.

- [ ] **Step 4: Commit**

```bash
git add src/web/src/app/globals.css src/web/tailwind.config.ts
git commit -m "$(cat <<'EOF'
feat(web): add Night Dispatch marketing theme tokens

Fixed dark-forward CSS custom properties for the F13 marketing landing
page, scoped as their own marketing-* Tailwind color group, independent
of the app's light/dark Ledger Counter tokens.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 3: Shared reveal-motion hook + test setup

**Files:**
- Create: `src/features/marketing/lib/motionVariants.ts`
- Test: `src/features/marketing/lib/motionVariants.test.tsx`
- Modify: `D:\courier\src\web\vitest.setup.ts`

**Interfaces:**
- Produces: `useRevealVariants(): { container: Variants; item: Variants; prefersReducedMotion: boolean }` — consumed by every animated section component (Tasks 6–11).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/lib/motionVariants.test.tsx`:

```tsx
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useRevealVariants } from "./motionVariants";

function mockMatchMedia(matches: boolean) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

describe("useRevealVariants", () => {
  afterEach(() => {
    mockMatchMedia(false);
  });

  it("returns animated item variants when motion is allowed", () => {
    mockMatchMedia(false);
    const { result } = renderHook(() => useRevealVariants());
    expect(result.current.prefersReducedMotion).toBe(false);
    expect(result.current.item.hidden).toEqual({ opacity: 0, y: 24 });
  });

  it("returns static, fully-visible item variants when prefers-reduced-motion is set", () => {
    mockMatchMedia(true);
    const { result } = renderHook(() => useRevealVariants());
    expect(result.current.prefersReducedMotion).toBe(true);
    expect(result.current.item.hidden).toEqual({ opacity: 1, y: 0 });
    expect(result.current.item.visible).toEqual({ opacity: 1, y: 0 });
  });

  it("always returns a container with staggerChildren", () => {
    mockMatchMedia(false);
    const { result } = renderHook(() => useRevealVariants());
    expect(result.current.container.visible).toEqual({ transition: { staggerChildren: 0.12 } });
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run (from `D:\courier\src\web`): `npx vitest run src/features/marketing/lib/motionVariants.test.tsx`
Expected: FAIL — `motionVariants.ts` does not exist yet.

- [ ] **Step 3: Implement the hook**

Create `src/features/marketing/lib/motionVariants.ts`:

```ts
"use client";

import { useReducedMotion } from "framer-motion";
import type { Variants } from "framer-motion";

export const containerVariants: Variants = {
  hidden: {},
  visible: { transition: { staggerChildren: 0.12 } },
};

export const itemVariants: Variants = {
  hidden: { opacity: 0, y: 24 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.5, ease: "easeOut" } },
};

const staticItemVariants: Variants = {
  hidden: { opacity: 1, y: 0 },
  visible: { opacity: 1, y: 0 },
};

export interface RevealVariants {
  container: Variants;
  item: Variants;
  prefersReducedMotion: boolean;
}

/**
 * Shared scroll-reveal variants for every marketing section. Falls back to a
 * fully-visible static state under prefers-reduced-motion, mirroring the
 * fallback AuthLifecycleGraphic already established for the two auth surfaces.
 */
export function useRevealVariants(): RevealVariants {
  const prefersReducedMotion = useReducedMotion();
  return {
    container: containerVariants,
    item: prefersReducedMotion ? staticItemVariants : itemVariants,
    prefersReducedMotion: Boolean(prefersReducedMotion),
  };
}
```

- [ ] **Step 4: Add an IntersectionObserver stub to the shared test setup**

Framer Motion's `whileInView` prop (used by every section from Task 6 onward) checks for `IntersectionObserver`, which jsdom does not implement. Add this block to `D:\courier\src\web\vitest.setup.ts`, after the existing `ResizeObserver` stub (after the `Object.defineProperty(HTMLElement.prototype, "getBoundingClientRect", ...)` block, before the pointer-capture block):

```ts
// jsdom doesn't implement IntersectionObserver — framer-motion's whileInView prop
// (every marketing section, Frontend Phase F13) checks for it before observing.
if (typeof window !== "undefined" && !window.IntersectionObserver) {
  window.IntersectionObserver = class {
    constructor(_callback: IntersectionObserverCallback, _options?: IntersectionObserverInit) {}
    observe() {}
    unobserve() {}
    disconnect() {}
    takeRecords(): IntersectionObserverEntry[] {
      return [];
    }
    root = null;
    rootMargin = "";
    thresholds: number[] = [];
  } as unknown as typeof IntersectionObserver;
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/lib/motionVariants.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/marketing/lib/motionVariants.ts src/web/src/features/marketing/lib/motionVariants.test.tsx src/web/vitest.setup.ts
git commit -m "$(cat <<'EOF'
feat(web): add shared reveal-motion hook for the marketing page

useRevealVariants() gives every F13 section the same scroll-triggered
stagger animation with a prefers-reduced-motion static fallback.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 4: Marketing copy/content data module

**Files:**
- Create: `src/features/marketing/content.ts`
- Test: `src/features/marketing/content.test.ts`

**Interfaces:**
- Produces: `NAV_LINKS`, `FEATURES`, `HOW_IT_WORKS_STEPS`, `WALKTHROUGH_STOPS`, `CREDIBILITY_POINTS`, `FOOTER_LINKS` (typed arrays), `getPrimaryCta(isAuthenticated: boolean): { href: string; label: string }` — consumed by every component task (5–12).
- Consumes: `Building2`, `ScanLine`, `Truck`, `Wallet`, `AlertTriangle`, `MapPinned` from `lucide-react` (verified to exist in the installed version).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/content.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { getPrimaryCta, FEATURES, HOW_IT_WORKS_STEPS, WALKTHROUGH_STOPS, CREDIBILITY_POINTS, FOOTER_LINKS, NAV_LINKS } from "./content";

describe("getPrimaryCta", () => {
  it("returns the sign-in CTA when unauthenticated", () => {
    expect(getPrimaryCta(false)).toEqual({ href: "/login", label: "Sign in" });
  });

  it("returns the dashboard CTA when authenticated", () => {
    expect(getPrimaryCta(true)).toEqual({ href: "/dashboard", label: "Go to dashboard" });
  });
});

describe("marketing content data", () => {
  it("has exactly 6 features, all with an icon, title, and description", () => {
    expect(FEATURES).toHaveLength(6);
    for (const feature of FEATURES) {
      expect(feature.icon).toBeDefined();
      expect(feature.title.length).toBeGreaterThan(0);
      expect(feature.description.length).toBeGreaterThan(0);
    }
  });

  it("has exactly 4 how-it-works steps, numbered 01-04", () => {
    expect(HOW_IT_WORKS_STEPS.map((s) => s.step)).toEqual(["01", "02", "03", "04"]);
  });

  it("has exactly 4 walkthrough stops ending in Delivered", () => {
    expect(WALKTHROUGH_STOPS).toHaveLength(4);
    expect(WALKTHROUGH_STOPS[3].label).toBe("Delivered");
    expect(WALKTHROUGH_STOPS[3].tone).toBe("success");
  });

  it("has at least 4 credibility points and none reference customers, logos, or numeric usage stats", () => {
    expect(CREDIBILITY_POINTS.length).toBeGreaterThanOrEqual(4);
    const bannedWords = /customer|testimonial|clients?|logo|\d+\s*(deliveries|shipments|companies|users)/i;
    for (const point of CREDIBILITY_POINTS) {
      expect(point.title).not.toMatch(bannedWords);
      expect(point.description).not.toMatch(bannedWords);
    }
  });

  it("footer links point at real existing routes only", () => {
    const hrefs = FOOTER_LINKS.map((l) => l.href);
    expect(hrefs).toEqual(["/login", "/merchant-login", "/track"]);
  });

  it("nav links include the public tracking route", () => {
    expect(NAV_LINKS.some((l) => l.href === "/track")).toBe(true);
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/content.test.ts`
Expected: FAIL — `content.ts` does not exist yet.

- [ ] **Step 3: Implement the content module**

Create `src/features/marketing/content.ts`:

```ts
import { AlertTriangle, Building2, MapPinned, ScanLine, Truck, Wallet, type LucideIcon } from "lucide-react";

export interface NavLink {
  href: string;
  label: string;
}

export const NAV_LINKS: NavLink[] = [
  { href: "#features", label: "Features" },
  { href: "#how-it-works", label: "How it works" },
  { href: "/track", label: "Track a parcel" },
];

export interface CtaLink {
  href: string;
  label: string;
}

export function getPrimaryCta(isAuthenticated: boolean): CtaLink {
  return isAuthenticated ? { href: "/dashboard", label: "Go to dashboard" } : { href: "/login", label: "Sign in" };
}

export interface MarketingFeature {
  icon: LucideIcon;
  title: string;
  description: string;
}

export const FEATURES: MarketingFeature[] = [
  {
    icon: Building2,
    title: "Multi-branch & role-based access",
    description:
      "Organizations, branches, and permission-based roles — every user scoped to exactly the branches and actions they're allowed.",
  },
  {
    icon: ScanLine,
    title: "Scan-driven custody",
    description:
      "Barcode and QR scanning for receive, sort, dispatch, and transfer — every custody change is a real scan, never an edit.",
  },
  {
    icon: Truck,
    title: "Manifests & hub transfers",
    description:
      "Sealed manifests move shipments branch to branch, with missing-shipment reconciliation on every receive.",
  },
  {
    icon: Wallet,
    title: "COD lifecycle & settlement",
    description:
      "Collection through rider deposit, branch reconciliation, and merchant settlement — every rupee traceable.",
  },
  {
    icon: AlertTriangle,
    title: "NDR & RTO management",
    description: "Structured non-delivery follow-up and return-to-origin workflows, not a dead-end failed status.",
  },
  {
    icon: MapPinned,
    title: "Nepal-specific geography & pricing",
    description:
      "Province, District, Local Level, and Ward drive delivery zones and rate cards — never free-text addresses.",
  },
];

export interface HowItWorksStep {
  step: string;
  title: string;
  description: string;
}

export const HOW_IT_WORKS_STEPS: HowItWorksStep[] = [
  { step: "01", title: "Book", description: "A shipment is created with an AWB, sender, recipient, and declared value." },
  {
    step: "02",
    title: "Scan & sort",
    description: "Origin branch receive, manifest, and hub-to-hub transfer — every step scanned.",
  },
  {
    step: "03",
    title: "Deliver & prove",
    description: "Assigned to a rider, delivery attempted, proof of delivery captured.",
  },
  { step: "04", title: "Reconcile", description: "COD collected, deposited, reconciled, and settled to the merchant." },
];

export type WalkthroughTone = "neutral" | "motion" | "success";

export interface WalkthroughStop {
  label: string;
  caption: string;
  tone: WalkthroughTone;
}

export const WALKTHROUGH_STOPS: WalkthroughStop[] = [
  { label: "Created", caption: "AWB NP2026-000000 booked at the origin counter.", tone: "neutral" },
  { label: "In transit", caption: "Scanned through the origin-to-destination hub transfer.", tone: "motion" },
  { label: "Out for delivery", caption: "Assigned to a rider, delivery attempt logged.", tone: "motion" },
  { label: "Delivered", caption: "Proof of delivery captured, COD reconciled.", tone: "success" },
];

export interface CredibilityPoint {
  title: string;
  description: string;
}

export const CREDIBILITY_POINTS: CredibilityPoint[] = [
  {
    title: "Real Nepal geography",
    description: "Province, District, Local Level, and Ward drive coverage and pricing — never free text.",
  },
  {
    title: "NPR-only, fully traceable COD",
    description: "Every collection, deposit, and settlement is recorded with before/after values and a reason for any adjustment.",
  },
  {
    title: "Status changes are real operations",
    description: "A shipment only moves forward through an actual scan, manifest, or delivery attempt — never an arbitrary admin edit.",
  },
  {
    title: "Audited, not assumed",
    description: "Sensitive changes — status corrections, COD adjustments, role changes — are logged with who, when, and why.",
  },
];

export const FOOTER_LINKS: NavLink[] = [
  { href: "/login", label: "Staff sign in" },
  { href: "/merchant-login", label: "Merchant sign in" },
  { href: "/track", label: "Track a parcel" },
];
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/content.test.ts`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/content.ts src/web/src/features/marketing/content.test.ts
git commit -m "$(cat <<'EOF'
feat(web): add marketing landing page copy/content data

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 5: `MarketingNav` component

**Files:**
- Create: `src/features/marketing/components/MarketingNav.tsx`
- Test: `src/features/marketing/components/MarketingNav.test.tsx`

**Interfaces:**
- Consumes: `NAV_LINKS`, `getPrimaryCta` from `../content` (Task 4).
- Produces: `MarketingNav({ isAuthenticated }: { isAuthenticated: boolean })` — consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/MarketingNav.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MarketingNav } from "./MarketingNav";

describe("MarketingNav", () => {
  it("shows Sign in linking to /login when unauthenticated", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
  });

  it("shows Go to dashboard linking to /dashboard when authenticated", () => {
    render(<MarketingNav isAuthenticated />);
    expect(screen.getByRole("link", { name: "Go to dashboard" })).toHaveAttribute("href", "/dashboard");
  });

  it("renders the Track a parcel link to /track", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "Track a parcel" })).toHaveAttribute("href", "/track");
  });

  it("renders the Courier Platform wordmark", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByText("Courier Platform")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/MarketingNav.test.tsx`
Expected: FAIL — `MarketingNav.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/MarketingNav.tsx`:

```tsx
"use client";

import Link from "next/link";
import { Package } from "lucide-react";
import { NAV_LINKS, getPrimaryCta } from "../content";

export function MarketingNav({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);

  return (
    <header className="sticky top-0 z-40 border-b border-marketing-border/60 bg-marketing-bg/90 backdrop-blur">
      <nav className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4">
        <Link href="/" className="flex items-center gap-2">
          <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-marketing-accent">
            <Package className="h-4 w-4 text-marketing-accent-fg" strokeWidth={2} />
          </span>
          <span className="font-display text-base font-semibold tracking-tight text-marketing-fg">
            Courier Platform
          </span>
        </Link>
        <div className="hidden items-center gap-6 md:flex">
          {NAV_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="text-sm text-marketing-muted-fg transition-colors hover:text-marketing-fg"
            >
              {link.label}
            </Link>
          ))}
        </div>
        <Link
          href={cta.href}
          className="rounded-lg bg-marketing-accent px-4 py-2 text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90"
        >
          {cta.label}
        </Link>
      </nav>
    </header>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/MarketingNav.test.tsx`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/MarketingNav.tsx src/web/src/features/marketing/components/MarketingNav.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add MarketingNav for the F13 landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 6: `Hero` component

**Files:**
- Create: `src/features/marketing/components/Hero.tsx`
- Test: `src/features/marketing/components/Hero.test.tsx`

**Interfaces:**
- Consumes: `getPrimaryCta` from `../content` (Task 4), `useRevealVariants` from `../lib/motionVariants` (Task 3).
- Produces: `Hero({ isAuthenticated }: { isAuthenticated: boolean })` — consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/Hero.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Hero } from "./Hero";

describe("Hero", () => {
  it("renders the headline as the page's one h1", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Every parcel, accounted for.");
  });

  it("links the primary CTA to /login when unauthenticated", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
  });

  it("links the primary CTA to /dashboard when authenticated", () => {
    render(<Hero isAuthenticated />);
    expect(screen.getByRole("link", { name: /go to dashboard/i })).toHaveAttribute("href", "/dashboard");
  });

  it("links the secondary CTA to the walkthrough anchor", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "See how it works" })).toHaveAttribute("href", "#walkthrough");
  });

  it("shows the hero status preview chip", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByTestId("hero-status-preview")).toHaveTextContent("Created");
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/Hero.test.tsx`
Expected: FAIL — `Hero.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/Hero.tsx`:

```tsx
"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { ArrowRight } from "lucide-react";
import { getPrimaryCta } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function Hero({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { container, item } = useRevealVariants();

  return (
    <section className="mx-auto flex max-w-6xl flex-col items-center gap-8 px-4 pb-24 pt-20 text-center">
      <motion.div variants={container} initial="hidden" animate="visible" className="flex flex-col items-center gap-6">
        <motion.h1
          variants={item}
          className="font-display text-5xl font-semibold tracking-tight text-marketing-fg sm:text-6xl lg:text-7xl"
        >
          Every parcel, <span className="text-marketing-accent">accounted for.</span>
        </motion.h1>
        <motion.p variants={item} className="max-w-2xl text-lg text-marketing-muted-fg">
          Scan-driven custody, hub transfers, delivery attempts, and COD settlement — one platform that always
          knows where a parcel is, who has it, and what happens next.
        </motion.p>
        <motion.div variants={item} className="flex flex-wrap items-center justify-center gap-4">
          <Link
            href={cta.href}
            className="group inline-flex items-center gap-2 rounded-lg bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90"
          >
            {cta.label}
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
          </Link>
          <a
            href="#walkthrough"
            className="rounded-lg border border-marketing-border px-6 py-3 text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg-elevated"
          >
            See how it works
          </a>
        </motion.div>
        <motion.div
          variants={item}
          data-testid="hero-status-preview"
          className="mt-4 flex items-center gap-3 rounded-lg border border-marketing-border bg-marketing-bg-elevated px-4 py-3"
        >
          <span className="h-2 w-2 rounded-full bg-marketing-accent" aria-hidden="true" />
          <span className="font-mono text-xs text-marketing-muted-fg">AWB NP2026-000000</span>
          <span className="text-xs font-medium text-marketing-fg">Created</span>
        </motion.div>
      </motion.div>
    </section>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/Hero.test.tsx`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/Hero.tsx src/web/src/features/marketing/components/Hero.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add Hero section for the F13 landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 7: `FeatureGrid` component

**Files:**
- Create: `src/features/marketing/components/FeatureGrid.tsx`
- Test: `src/features/marketing/components/FeatureGrid.test.tsx`

**Interfaces:**
- Consumes: `FEATURES` from `../content` (Task 4), `useRevealVariants` from `../lib/motionVariants` (Task 3).
- Produces: `FeatureGrid()` (no props) — consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/FeatureGrid.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { FeatureGrid } from "./FeatureGrid";
import { FEATURES } from "../content";

describe("FeatureGrid", () => {
  it("renders every feature's title and description", () => {
    render(<FeatureGrid />);
    for (const feature of FEATURES) {
      expect(screen.getByText(feature.title)).toBeInTheDocument();
      expect(screen.getByText(feature.description)).toBeInTheDocument();
    }
  });

  it("has the #features anchor id for nav scrolling", () => {
    const { container } = render(<FeatureGrid />);
    expect(container.querySelector("#features")).not.toBeNull();
  });

  it("renders a section heading", () => {
    render(<FeatureGrid />);
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/FeatureGrid.test.tsx`
Expected: FAIL — `FeatureGrid.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/FeatureGrid.tsx`:

```tsx
"use client";

import { motion } from "framer-motion";
import { FEATURES } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function FeatureGrid() {
  const { container, item } = useRevealVariants();

  return (
    <section id="features" className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Everything a courier operation actually runs on
        </h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          Not an order tracker bolted onto a spreadsheet — the physical lifecycle of a parcel, modeled end to end.
        </p>
      </div>
      <motion.div
        variants={container}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.2 }}
        className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3"
      >
        {FEATURES.map((feature) => (
          <motion.div
            key={feature.title}
            variants={item}
            className="flex flex-col gap-3 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6"
          >
            <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-marketing-accent/15">
              <feature.icon className="h-5 w-5 text-marketing-accent" strokeWidth={2} />
            </span>
            <h3 className="font-display text-base font-semibold text-marketing-fg">{feature.title}</h3>
            <p className="text-sm text-marketing-muted-fg">{feature.description}</p>
          </motion.div>
        ))}
      </motion.div>
    </section>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/FeatureGrid.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/FeatureGrid.tsx src/web/src/features/marketing/components/FeatureGrid.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add FeatureGrid section for the F13 landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 8: `HowItWorks` component

**Files:**
- Create: `src/features/marketing/components/HowItWorks.tsx`
- Test: `src/features/marketing/components/HowItWorks.test.tsx`

**Interfaces:**
- Consumes: `HOW_IT_WORKS_STEPS` from `../content` (Task 4), `useRevealVariants` from `../lib/motionVariants` (Task 3).
- Produces: `HowItWorks()` (no props) — consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/HowItWorks.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { HowItWorks } from "./HowItWorks";
import { HOW_IT_WORKS_STEPS } from "../content";

describe("HowItWorks", () => {
  it("renders all 4 steps as an ordered list of 4 items", () => {
    render(<HowItWorks />);
    expect(screen.getAllByRole("listitem")).toHaveLength(4);
  });

  it("renders every step's title and description", () => {
    render(<HowItWorks />);
    for (const step of HOW_IT_WORKS_STEPS) {
      expect(screen.getByText(step.title)).toBeInTheDocument();
      expect(screen.getByText(step.description)).toBeInTheDocument();
    }
  });

  it("has the #how-it-works anchor id for nav scrolling", () => {
    const { container } = render(<HowItWorks />);
    expect(container.querySelector("#how-it-works")).not.toBeNull();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/HowItWorks.test.tsx`
Expected: FAIL — `HowItWorks.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/HowItWorks.tsx`:

```tsx
"use client";

import { motion } from "framer-motion";
import { HOW_IT_WORKS_STEPS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function HowItWorks() {
  const { container, item } = useRevealVariants();

  return (
    <section id="how-it-works" className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">How it works</h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          Four stages, each backed by a real scan, manifest, or delivery attempt — never an arbitrary status edit.
        </p>
      </div>
      <motion.ol
        variants={container}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.2 }}
        className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4"
      >
        {HOW_IT_WORKS_STEPS.map((step) => (
          <motion.li
            key={step.step}
            variants={item}
            className="flex flex-col gap-2 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6"
          >
            <span className="font-mono text-xs text-marketing-accent">{step.step}</span>
            <h3 className="font-display text-base font-semibold text-marketing-fg">{step.title}</h3>
            <p className="text-sm text-marketing-muted-fg">{step.description}</p>
          </motion.li>
        ))}
      </motion.ol>
    </section>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/HowItWorks.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/HowItWorks.tsx src/web/src/features/marketing/components/HowItWorks.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add HowItWorks section for the F13 landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 9: `ProductWalkthrough` component (animated centerpiece)

**Files:**
- Create: `src/features/marketing/components/ProductWalkthrough.tsx`
- Test: `src/features/marketing/components/ProductWalkthrough.test.tsx`

**Interfaces:**
- Consumes: `WALKTHROUGH_STOPS`, `WalkthroughTone` from `../content` (Task 4), `useRevealVariants` from `../lib/motionVariants` (Task 3).
- Produces: `ProductWalkthrough()` (no props), rendering a `<section id="walkthrough">` — the `#walkthrough` anchor is consumed by `Hero`'s secondary CTA (Task 6). Consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/ProductWalkthrough.test.tsx`:

```tsx
import { act, afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ProductWalkthrough } from "./ProductWalkthrough";
import { WALKTHROUGH_STOPS } from "../content";

function mockMatchMedia(matches: boolean) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

describe("ProductWalkthrough", () => {
  afterEach(() => {
    mockMatchMedia(false);
    vi.useRealTimers();
  });

  it("shows every stop statically, all at once, when reduced motion is preferred", () => {
    mockMatchMedia(true);
    render(<ProductWalkthrough />);
    for (const stop of WALKTHROUGH_STOPS) {
      expect(screen.getByText(stop.label)).toBeInTheDocument();
    }
    expect(screen.queryByTestId("walkthrough-frame")).not.toBeInTheDocument();
  });

  it("starts on the first stop when motion is allowed", () => {
    mockMatchMedia(false);
    render(<ProductWalkthrough />);
    expect(screen.getByTestId("walkthrough-frame")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });

  it("advances to the next stop after the cycle interval", () => {
    mockMatchMedia(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800);
    });
    expect(screen.getByTestId("walkthrough-frame")).toHaveTextContent(WALKTHROUGH_STOPS[1].label);
  });

  it("wraps back to the first stop after cycling through all four", () => {
    mockMatchMedia(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800 * WALKTHROUGH_STOPS.length);
    });
    expect(screen.getByTestId("walkthrough-frame")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/ProductWalkthrough.test.tsx`
Expected: FAIL — `ProductWalkthrough.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/ProductWalkthrough.tsx`:

```tsx
"use client";

import { useEffect, useState } from "react";
import { motion } from "framer-motion";
import { WALKTHROUGH_STOPS, type WalkthroughTone } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

// Literal HSL values, not the shared --status-* tokens: these are the app's own
// dark-mode status variants (globals.css .dark block), reused directly because this
// section always sits on the fixed --marketing-bg (near-black) regardless of visitor
// theme — the same reasoning AuthLifecycleGraphic documents for the auth panel.
// Verified against --marketing-bg (228 20% 6%): created 8.14:1, in-transit/out-for-
// delivery 9.38:1, delivered 7.53:1 — all clear both the 3:1 non-text and 4.5:1 text
// floors. See the plan's "Verified Design Tokens" table.
const TONE_COLOR: Record<WalkthroughTone, string> = {
  neutral: "hsl(234.3 13.2% 68.8%)",
  motion: "hsl(38.0 78.9% 57.3%)",
  success: "hsl(169.7 47.9% 47.5%)",
};

const CYCLE_MS = 2800;

export function ProductWalkthrough() {
  const { prefersReducedMotion } = useRevealVariants();
  const [activeIndex, setActiveIndex] = useState(0);

  useEffect(() => {
    if (prefersReducedMotion) return;
    const id = window.setInterval(() => {
      setActiveIndex((current) => (current + 1) % WALKTHROUGH_STOPS.length);
    }, CYCLE_MS);
    return () => window.clearInterval(id);
  }, [prefersReducedMotion]);

  if (prefersReducedMotion) {
    return (
      <section id="walkthrough" className="mx-auto max-w-3xl px-4 py-24">
        <h2 className="mb-8 text-center font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          One parcel, every step accounted for
        </h2>
        <ul className="flex flex-col gap-4 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6">
          {WALKTHROUGH_STOPS.map((stop) => (
            <li key={stop.label} className="flex items-center gap-3">
              <span
                className="h-2 w-2 shrink-0 rounded-full"
                style={{ backgroundColor: TONE_COLOR[stop.tone] }}
                aria-hidden="true"
              />
              <span className="text-sm font-medium text-marketing-fg">{stop.label}</span>
              <span className="text-sm text-marketing-muted-fg">{stop.caption}</span>
            </li>
          ))}
        </ul>
      </section>
    );
  }

  const activeStop = WALKTHROUGH_STOPS[activeIndex];

  return (
    <section id="walkthrough" className="mx-auto max-w-3xl px-4 py-24">
      <h2 className="mb-8 text-center font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
        One parcel, every step accounted for
      </h2>
      <div data-testid="walkthrough-frame" className="rounded-xl border border-marketing-border bg-marketing-bg-elevated p-8">
        <motion.div
          key={activeStop.label}
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4 }}
          className="flex flex-col items-center gap-4 text-center"
        >
          <span
            className="h-3 w-3 rounded-full"
            style={{ backgroundColor: TONE_COLOR[activeStop.tone] }}
            aria-hidden="true"
          />
          <p className="font-mono text-xs text-marketing-muted-fg">AWB NP2026-000000</p>
          <p className="font-display text-xl font-semibold text-marketing-fg">{activeStop.label}</p>
          <p className="text-sm text-marketing-muted-fg">{activeStop.caption}</p>
        </motion.div>
        <div className="mt-6 flex items-center justify-center gap-2">
          {WALKTHROUGH_STOPS.map((stop, index) => (
            <span
              key={stop.label}
              data-testid="walkthrough-dot"
              className="h-1.5 w-1.5 rounded-full transition-opacity"
              style={{ backgroundColor: TONE_COLOR[stop.tone], opacity: index === activeIndex ? 1 : 0.3 }}
              aria-hidden="true"
            />
          ))}
        </div>
      </div>
    </section>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/ProductWalkthrough.test.tsx`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/ProductWalkthrough.tsx src/web/src/features/marketing/components/ProductWalkthrough.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add animated ProductWalkthrough centerpiece for F13

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 10: `Credibility` component

**Files:**
- Create: `src/features/marketing/components/Credibility.tsx`
- Test: `src/features/marketing/components/Credibility.test.tsx`

**Interfaces:**
- Consumes: `CREDIBILITY_POINTS` from `../content` (Task 4), `useRevealVariants` from `../lib/motionVariants` (Task 3).
- Produces: `Credibility()` (no props) — consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/Credibility.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Credibility } from "./Credibility";
import { CREDIBILITY_POINTS } from "../content";

describe("Credibility", () => {
  it("renders every credibility point's title and description", () => {
    render(<Credibility />);
    for (const point of CREDIBILITY_POINTS) {
      expect(screen.getByText(point.title)).toBeInTheDocument();
      expect(screen.getByText(point.description)).toBeInTheDocument();
    }
  });

  it("explicitly states there are no case studies yet, rather than fabricating any", () => {
    render(<Credibility />);
    expect(screen.getByText(/no case studies yet/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/Credibility.test.tsx`
Expected: FAIL — `Credibility.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/Credibility.tsx`:

```tsx
"use client";

import { motion } from "framer-motion";
import { CREDIBILITY_POINTS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function Credibility() {
  const { container, item } = useRevealVariants();

  return (
    <section className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Built for how Nepal courier operations actually work
        </h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          No case studies yet — this is still an early platform. What's already true, today:
        </p>
      </div>
      <motion.div
        variants={container}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.2 }}
        className="grid grid-cols-1 gap-6 sm:grid-cols-2"
      >
        {CREDIBILITY_POINTS.map((point) => (
          <motion.div
            key={point.title}
            variants={item}
            className="rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6"
          >
            <h3 className="font-display text-base font-semibold text-marketing-fg">{point.title}</h3>
            <p className="mt-2 text-sm text-marketing-muted-fg">{point.description}</p>
          </motion.div>
        ))}
      </motion.div>
    </section>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/Credibility.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/Credibility.tsx src/web/src/features/marketing/components/Credibility.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add Credibility section for the F13 landing page

Replaces conventional social proof with an honest "no case studies
yet" domain-credibility section, per PRODUCT.md's no-fabrication rule.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 11: `FinalCta` component

**Files:**
- Create: `src/features/marketing/components/FinalCta.tsx`
- Test: `src/features/marketing/components/FinalCta.test.tsx`

**Interfaces:**
- Consumes: `getPrimaryCta` from `../content` (Task 4), `useRevealVariants` from `../lib/motionVariants` (Task 3).
- Produces: `FinalCta({ isAuthenticated }: { isAuthenticated: boolean })` — consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/FinalCta.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { FinalCta } from "./FinalCta";

describe("FinalCta", () => {
  it("links to /login when unauthenticated", () => {
    render(<FinalCta isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
  });

  it("links to /dashboard when authenticated", () => {
    render(<FinalCta isAuthenticated />);
    expect(screen.getByRole("link", { name: /go to dashboard/i })).toHaveAttribute("href", "/dashboard");
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/FinalCta.test.tsx`
Expected: FAIL — `FinalCta.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/FinalCta.tsx`:

```tsx
"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { getPrimaryCta } from "../content";
import { useRevealVariants } from "../lib/motionVariants";

export function FinalCta({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { item } = useRevealVariants();

  return (
    <section className="mx-auto max-w-3xl px-4 py-24 text-center">
      <motion.div
        variants={item}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.4 }}
        className="flex flex-col items-center gap-6"
      >
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Run your courier operation on one system
        </h2>
        <p className="max-w-xl text-marketing-muted-fg">
          Branches, riders, merchants, and COD — all traceable, from booking to settlement.
        </p>
        <Link
          href={cta.href}
          className="rounded-lg bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90"
        >
          {cta.label}
        </Link>
      </motion.div>
    </section>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/FinalCta.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/FinalCta.tsx src/web/src/features/marketing/components/FinalCta.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add FinalCta section for the F13 landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 12: `MarketingFooter` component

**Files:**
- Create: `src/features/marketing/components/MarketingFooter.tsx`
- Test: `src/features/marketing/components/MarketingFooter.test.tsx`

**Interfaces:**
- Consumes: `FOOTER_LINKS` from `../content` (Task 4).
- Produces: `MarketingFooter()` (no props, no client hooks — plain server component) — consumed by `app/page.tsx` (Task 13).

- [ ] **Step 1: Write the failing test**

Create `src/features/marketing/components/MarketingFooter.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MarketingFooter } from "./MarketingFooter";
import { FOOTER_LINKS } from "../content";

describe("MarketingFooter", () => {
  it("renders every footer link with the correct href", () => {
    render(<MarketingFooter />);
    for (const link of FOOTER_LINKS) {
      expect(screen.getByRole("link", { name: link.label })).toHaveAttribute("href", link.href);
    }
  });

  it("renders the Courier Platform wordmark", () => {
    render(<MarketingFooter />);
    expect(screen.getByText("Courier Platform")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/features/marketing/components/MarketingFooter.test.tsx`
Expected: FAIL — `MarketingFooter.tsx` does not exist yet.

- [ ] **Step 3: Implement the component**

Create `src/features/marketing/components/MarketingFooter.tsx`:

```tsx
import Link from "next/link";
import { FOOTER_LINKS } from "../content";

export function MarketingFooter() {
  return (
    <footer className="border-t border-marketing-border/60 px-4 py-10">
      <div className="mx-auto flex max-w-6xl flex-col items-center justify-between gap-4 sm:flex-row">
        <span className="font-display text-sm font-semibold text-marketing-fg">Courier Platform</span>
        <div className="flex flex-wrap items-center justify-center gap-6">
          {FOOTER_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="text-sm text-marketing-muted-fg transition-colors hover:text-marketing-fg"
            >
              {link.label}
            </Link>
          ))}
        </div>
      </div>
    </footer>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/features/marketing/components/MarketingFooter.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/MarketingFooter.tsx src/web/src/features/marketing/components/MarketingFooter.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): add MarketingFooter for the F13 landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 13: Wire up `/` — replace the redirect with the composed marketing page

**Files:**
- Modify: `src/app/page.tsx`
- Modify: `src/app/page.test.tsx`

**Interfaces:**
- Consumes: `getValidAccessToken` from `@/lib/auth/session` (existing, `Promise<string | null>`), `MarketingNav` (Task 5), `Hero` (Task 6), `FeatureGrid` (Task 7), `HowItWorks` (Task 8), `ProductWalkthrough` (Task 9), `Credibility` (Task 10), `FinalCta` (Task 11), `MarketingFooter` (Task 12).

- [ ] **Step 1: Write the failing test (rewriting the old redirect-based test)**

Replace the full contents of `src/app/page.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/auth/session", () => ({
  getValidAccessToken: vi.fn(),
}));

import RootPage from "./page";
import { getValidAccessToken } from "@/lib/auth/session";

describe("RootPage", () => {
  it("renders the marketing page with a Go to dashboard CTA when a session exists", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue("token-123");

    const page = await RootPage();
    render(page);

    expect(screen.getAllByRole("link", { name: /go to dashboard/i }).length).toBeGreaterThan(0);
    expect(screen.queryAllByRole("link", { name: /^sign in$/i })).toHaveLength(0);
  });

  it("renders the marketing page with a Sign in CTA when there is no session", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue(null);

    const page = await RootPage();
    render(page);

    expect(screen.getAllByRole("link", { name: /^sign in$/i }).length).toBeGreaterThan(0);
    expect(screen.queryAllByRole("link", { name: /go to dashboard/i })).toHaveLength(0);
  });

  it("renders exactly one h1 (the hero headline)", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue(null);

    const page = await RootPage();
    render(page);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  it("keeps heading levels in order with no skipped levels", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue(null);

    const page = await RootPage();
    render(page);

    const levels = screen.getAllByRole("heading").map((heading) => Number(heading.tagName.substring(1)));
    let maxSeen = levels[0];
    for (const level of levels) {
      if (level > maxSeen) {
        expect(level - maxSeen).toBeLessThanOrEqual(1);
      }
      maxSeen = Math.max(maxSeen, level);
    }
  });

  it("exposes header, main, and footer landmarks", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue(null);

    const page = await RootPage();
    render(page);

    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/app/page.test.tsx`
Expected: FAIL — `page.tsx` still redirects and calls `redirect()`, which the test no longer mocks.

- [ ] **Step 3: Replace `page.tsx`**

Replace the full contents of `src/app/page.tsx`:

```tsx
import { getValidAccessToken } from "@/lib/auth/session";
import { MarketingNav } from "@/features/marketing/components/MarketingNav";
import { Hero } from "@/features/marketing/components/Hero";
import { FeatureGrid } from "@/features/marketing/components/FeatureGrid";
import { HowItWorks } from "@/features/marketing/components/HowItWorks";
import { ProductWalkthrough } from "@/features/marketing/components/ProductWalkthrough";
import { Credibility } from "@/features/marketing/components/Credibility";
import { FinalCta } from "@/features/marketing/components/FinalCta";
import { MarketingFooter } from "@/features/marketing/components/MarketingFooter";

export default async function RootPage() {
  const accessToken = await getValidAccessToken();
  const isAuthenticated = Boolean(accessToken);

  return (
    <div className="min-h-screen bg-marketing-bg">
      <MarketingNav isAuthenticated={isAuthenticated} />
      <main>
        <Hero isAuthenticated={isAuthenticated} />
        <FeatureGrid />
        <HowItWorks />
        <ProductWalkthrough />
        <Credibility />
        <FinalCta isAuthenticated={isAuthenticated} />
      </main>
      <MarketingFooter />
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npx vitest run src/app/page.test.tsx`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/web/src/app/page.tsx src/web/src/app/page.test.tsx
git commit -m "$(cat <<'EOF'
feat(web): replace / redirect with the public marketing landing page

/ now renders the F13 marketing page for every visitor instead of
hard-redirecting to /login or /dashboard; the nav/hero/final CTAs are
auth-aware via a single server-side getValidAccessToken() check.
/login and /dashboard are otherwise unchanged.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

### Task 14: Full verification pass — build, lint, full test suite, no regressions

**Files:**
- None created or modified — verification only. If any check below fails, fix the specific file it points to and re-run that check before continuing; do not proceed to a commit with a red check.

- [ ] **Step 1: Run the full test suite**

Run (from `D:\courier\src\web`): `npm test`
Expected: every test file passes, including every pre-existing suite (no regressions) and all new `features/marketing/**` and `app/page.test.tsx` tests from Tasks 3–13.

- [ ] **Step 2: Run the linter**

Run: `npm run lint`
Expected: no errors. If `react-hooks/exhaustive-deps` or similar flags anything in `ProductWalkthrough.tsx`'s `useEffect` (Task 9), confirm the dependency array `[prefersReducedMotion]` is correct as-is (it intentionally excludes `WALKTHROUGH_STOPS.length`, a module-level constant) rather than suppressing the rule.

- [ ] **Step 3: Run the production build**

Run: `npm run build`
Expected: builds successfully. This is the first point in the plan where Tailwind's JIT actually scans the finished component files and generates real CSS for every `marketing-*` utility used since Task 5 — confirms Task 2's token wiring was correct end-to-end, not just type-valid.

- [ ] **Step 4: Grep the production CSS output for the new utilities**

Run: `grep -c "marketing-bg\|marketing-accent\|marketing-fg" .next/static/css/*.css`
Expected: a non-zero count, confirming the `marketing-*` Tailwind utilities were actually generated into the shipped stylesheet (not silently purged due to a content-glob or config mistake).

- [ ] **Step 5: Confirm no other route regressed**

Run: `npx vitest run src/app --exclude src/app/page.test.tsx` then `npx vitest run src/features --exclude "src/features/marketing/**"`
Expected: both pass — every other route/feature's existing tests are unaffected by this phase's changes.

- [ ] **Step 6: Update the frontend roadmap doc**

Read `docs/FRONTEND-ROADMAP.md`'s "Later" section (it currently reads *"All frontend phases planned through F12 are complete..."*). Add a new dated entry above it recording Phase F13 complete, following the exact structure and evidentiary style of the F12 entry already in that file (what was verified, live in a browser where possible, and what could not be verified in this environment — e.g. real Lighthouse/visual QA of the animation on a real viewport, if this sandbox has no way to screenshot it).

- [ ] **Step 7: Commit the roadmap update**

```bash
git add docs/FRONTEND-ROADMAP.md
git commit -m "$(cat <<'EOF'
docs: mark Frontend Phase F13 (marketing landing page) complete

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01CW7mk4dhHZ6VbnjgHFRkKv
EOF
)"
```

---

## Post-Plan: Manual/Visual Verification (not automated)

The automated tasks above prove the page renders correct content, correct links, and correct reduced-motion fallbacks — they cannot prove the animations *look* good. Before considering this phase truly done, per this repo's `verification-before-completion` standard, load `http://localhost:3000/` in a real browser (`npm run dev`) and confirm:

- The hero, feature grid, how-it-works, and credibility sections visibly stagger-reveal on scroll.
- The product walkthrough visibly cycles through all four stops.
- Toggling the OS/browser "reduce motion" setting (or Chrome DevTools' emulated `prefers-reduced-motion: reduce`) makes every section appear instantly, fully visible, with no animation.
- The page is usable and reasonably laid out at a mobile viewport width (375px) and a desktop width (1440px).
- Tab-key navigation reaches every nav link, both CTAs, and every footer link in a sensible order, with a visible focus ring.
