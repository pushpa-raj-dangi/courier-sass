# F15 R1 — Console Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the staff, merchant and platform consoles one shared `ConsoleShell` (dark grouped sidebar + new header), retune surface tokens, and add the page primitives R2–R6 will compose — frontend only.

**Architecture:** Pure nav helpers (`groupNavItems`, `findActiveItem`) feed a presentational `ConsoleShell` built on the existing shadcn `Sidebar`. Each surface (staff `AppShell`, `MerchantAppShell`, new `PlatformShell`) becomes a thin client wrapper that filters its own nav catalog by permission and passes plain `ConsoleNavGroup[]` in. Page primitives in `src/components/page/` are stateless; missing-data UI always renders `ComingSoonSlot`.

**Tech Stack:** Next.js 14 (app router), React 18, TypeScript, Tailwind + shadcn/ui (Radix), next-intl, TanStack Query, lucide-react, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-30-f15-r1-console-foundation-design.md`

All paths below are relative to `src/web/` unless they start with `docs/` or `DESIGN.md`. Run every command from `src/web/`.

## Global Constraints

- Frontend only: no API, DTO, migration or backend change.
- No new npm dependencies.
- Never render mock or invented numbers; data the API does not serve renders `ComingSoonSlot`.
- Do not change any `--status-*`, `--marketing-*`, `--auth-panel*` token, fonts, or radii.
- Marketing (`/`, `/pricing`), public `/track`, auth pages, rider shell: untouched.
- Every new text pair ≥ 4.5:1 contrast, non-text ≥ 3:1, light and dark (enforced by the Task 1 test).
- Every user-visible staff/console string goes through next-intl with both `messages/en.json` and `messages/ne.json` (the catalog-parity test enforces identical key sets). Merchant and platform nav labels stay English literals (existing convention).
- Use shadcn/ui components (Button, DropdownMenu, Popover, Breadcrumb, Sidebar, Card, Skeleton) — no hand-built equivalents.
- Status color is never the only carrier of meaning (label always present).
- Commit after each task; messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Nested routes pick the most specific nav item** — `/riders/live-map` highlights "Live Map", not "Riders"; `/billing/callback/abc` highlights "Billing"; `/platform/organizations/123` highlights "Organizations". Pinned in Task 2 (`findActiveItem` longest-match tests).
2. **A user whose permissions hide every item in a group** sees no orphan group heading, and a user with zero permissions sees an empty rail without crashing. Pinned in Task 2 (`groupNavItems` drops empty groups), Task 6 (sidebar skips empty groups) and Task 7 (staff hook with `[]` permissions).
3. **The date near midnight** — 18:30 UTC is already the next day in Kathmandu (UTC+5:45); both AD and BS must show the Kathmandu day. Pinned in Task 4.
4. **Collapsed icon rail** — group headings hide but every nav link keeps an accessible name. Pinned in Task 6.
5. **Nepali locale** — group headings and BS month names render in Nepali. Pinned in Task 4 and Task 7.

---

## File Structure

**Create**
- `src/app/tokens.contrast.test.ts` — parses `globals.css`, asserts WCAG ratios for every new token pair.
- `src/components/console/types.ts` — `ConsoleNavItem`, `ConsoleNavGroup`, `ConsoleNavGroupDef`, `ConsoleBrand`.
- `src/components/console/navModel.ts` (+ `.test.ts`) — `groupNavItems`, `findActiveItem`.
- `src/components/console/formatDualDate.ts` (+ `.test.ts`) — pure AD/BS formatter.
- `src/components/console/DualDate.tsx` (+ `.test.tsx`).
- `src/components/console/NotificationsButton.tsx` (+ `.test.tsx`).
- `src/components/console/ConsoleBreadcrumb.tsx` (+ `.test.tsx`).
- `src/components/console/ConsoleSidebar.tsx` (+ `.test.tsx`).
- `src/components/console/ConsoleShell.tsx` (+ `.test.tsx`).
- `src/components/page/ComingSoonSlot.tsx` (+ `.test.tsx`).
- `src/components/page/PageHeader.tsx`, `SectionCard.tsx`, `KpiTile.tsx`, `PipelineStrip.tsx`, `SheetHeaderBlock.tsx` (+ one `page.test.tsx` covering them).
- `src/components/layout/useStaffNavGroups.ts` (+ `.test.tsx`).
- `src/components/merchant-layout/merchantNav.ts`, `useMerchantNavGroups.ts` (+ `.test.tsx`).
- `src/features/platform/platformNav.ts`, `PlatformShell.tsx`, `PlatformUserMenu.tsx` (+ `PlatformUserMenu.test.tsx`, `platformNav.test.ts`).

**Modify**
- `src/app/globals.css`, `tailwind.config.ts` — tokens.
- `messages/en.json`, `messages/ne.json` — `console.*`, `nav.groups.*`; remove `nav.group`.
- `src/lib/navigation/navItems.ts` — `group` field, `STAFF_NAV_GROUPS`, `hasAnyPermission`.
- `src/components/layout/AppShell.tsx`, `BranchSwitcher.tsx`.
- `src/components/merchant-layout/MerchantAppShell.tsx`.
- `src/app/platform/layout.tsx`.
- `src/components/data-table/DataTable.tsx` (+ test), `BorderedTableContainer.tsx`, `src/components/status-badge/StatusBadge.tsx`, `src/components/states/EmptyState.tsx`, `LoadingState.tsx`, `StatTile.tsx`.
- `src/features/reports/components/DashboardHeader.tsx`.
- `src/app/merchant/api-keys/page.tsx`, `src/app/merchant/staff/page.tsx` — comment references only.
- `DESIGN.md`, `docs/FRONTEND-ROADMAP.md`.

**Delete**
- `src/components/app-sidebar.tsx`, `app-sidebar.test.tsx`, `nav-main.tsx`, `nav-main.test.tsx`.
- `src/components/merchant-layout/MerchantAppSidebar.tsx`, `MerchantNavMain.tsx`, `MerchantNavMain.test.tsx`.
- `src/features/platform/PlatformNav.tsx`, `PlatformNav.test.tsx`.

---

### Task 1: Surface tokens + contrast guard

**Files:**
- Modify: `src/app/globals.css` (`:root`, `.dark`, and the `@layer base` sidebar blocks)
- Modify: `tailwind.config.ts` (`theme.extend.colors`)
- Test: `src/app/tokens.contrast.test.ts`

**Interfaces:**
- Produces: CSS vars `--surface-sunken`, `--sidebar-muted`; Tailwind classes `bg-surface-sunken`, `text-sidebar-muted` (used by Tasks 6, 7, 11).

- [ ] **Step 0: Record the baseline**

Run: `npm test 2>&1 | tail -5`
Expected: all tests pass. Write the passing-test count into your task report so later tasks can compare.

- [ ] **Step 1: Write the failing contrast test**

Create `src/app/tokens.contrast.test.ts`:

```ts
import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Parses every `:root { ... }` and `.dark { ... }` block in globals.css (including the ones
// nested in @layer base — the regex only needs the innermost brace pair) into light/dark
// token maps. Dark inherits any token it does not override, matching CSS cascade.
function readTokens() {
  const css = readFileSync(path.resolve(process.cwd(), "src/app/globals.css"), "utf8");
  const light: Record<string, string> = {};
  const dark: Record<string, string> = {};
  const blockRe = /(:root|\.dark)\s*\{([^}]*)\}/g;
  for (const match of css.matchAll(blockRe)) {
    const target = match[1] === ":root" ? light : dark;
    for (const decl of match[2].matchAll(/--([\w-]+)\s*:\s*([^;]+);/g)) {
      target[decl[1]] = decl[2].trim();
    }
  }
  return { light, dark: { ...light, ...dark } };
}

function hslToRgb(value: string): [number, number, number] {
  const m = value.match(/^([\d.]+)\s+([\d.]+)%\s+([\d.]+)%$/);
  if (!m) throw new Error(`Not an HSL triple: "${value}"`);
  const h = Number(m[1]);
  const s = Number(m[2]) / 100;
  const l = Number(m[3]) / 100;
  const k = (n: number) => (n + h / 30) % 12;
  const a = s * Math.min(l, 1 - l);
  const f = (n: number) => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  return [f(0), f(8), f(4)];
}

function luminance([r, g, b]: [number, number, number]) {
  const c = (v: number) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
  return 0.2126 * c(r) + 0.7152 * c(g) + 0.0722 * c(b);
}

function ratio(a: string, b: string) {
  const [hi, lo] = [luminance(hslToRgb(a)), luminance(hslToRgb(b))].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const TEXT_PAIRS: [string, string][] = [
  ["foreground", "background"],
  ["foreground", "card"],
  ["muted-foreground", "background"],
  ["muted-foreground", "card"],
  ["muted-foreground", "surface-sunken"],
  ["status-delivered", "card"],
  ["destructive", "card"],
  ["sidebar-foreground", "sidebar-background"],
  ["sidebar-foreground", "sidebar-accent"],
  ["sidebar-accent-foreground", "sidebar-accent"],
  ["sidebar-muted", "sidebar-background"],
];
const NON_TEXT_PAIRS: [string, string][] = [["sidebar-primary", "sidebar-background"]];

describe("design tokens contrast", () => {
  const tokens = readTokens();
  for (const mode of ["light", "dark"] as const) {
    for (const [fg, bg] of TEXT_PAIRS) {
      it(`${mode}: --${fg} on --${bg} clears 4.5:1`, () => {
        expect(ratio(tokens[mode][fg], tokens[mode][bg])).toBeGreaterThanOrEqual(4.5);
      });
    }
    for (const [fg, bg] of NON_TEXT_PAIRS) {
      it(`${mode}: --${fg} on --${bg} clears 3:1`, () => {
        expect(ratio(tokens[mode][fg], tokens[mode][bg])).toBeGreaterThanOrEqual(3);
      });
    }
  }
  it("card differs from page background in light mode", () => {
    expect(tokens.light.card).not.toBe(tokens.light.background);
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/app/tokens.contrast.test.ts`
Expected: FAIL — `Not an HSL triple: "undefined"` for `surface-sunken`/`sidebar-muted`, and "card differs" fails.

- [ ] **Step 3: Update tokens**

In `src/app/globals.css`, first `:root` block, change:

```css
  --background: 42 31% 93.7%;
  --card: 40 100% 98.8%;
  --popover: 40 100% 98.8%;
```

and add directly after `--popover-foreground`:

```css
  /* F15 R1: sunken surface for table header rows and Sheet footer bars — a step below
     --background so it reads as recessed on both page and card. */
  --surface-sunken: 42 26% 92.5%;
```

In the first `.dark` block add after `--popover-foreground`:

```css
  --surface-sunken: 229 20% 12.5%;
```

Replace the `@layer base` sidebar block's values:

```css
  :root {
    --sidebar-background: 229 20% 10.8%;
    --sidebar-foreground: 231 12% 79%;
    --sidebar-primary: 236 73% 60%;
    --sidebar-primary-foreground: 0 0% 100%;
    --sidebar-accent: 230 22% 21.2%;
    --sidebar-accent-foreground: 0 0% 100%;
    --sidebar-border: 229 20% 21.6%;
    --sidebar-ring: 236 73% 70%;
    --sidebar-muted: 232 10% 58.4%;
  }
  .dark {
    --sidebar-background: 225 22% 7.1%;
    --sidebar-foreground: 231 12% 79%;
    --sidebar-primary: 236 73% 60%;
    --sidebar-primary-foreground: 0 0% 100%;
    --sidebar-accent: 230 22% 21.2%;
    --sidebar-accent-foreground: 0 0% 100%;
    --sidebar-border: 229 20% 21.6%;
    --sidebar-ring: 236 73% 70%;
    --sidebar-muted: 232 10% 58.4%;
  }
```

Replace the comment above that block with:

```css
/*
 * Sidebar tokens — F15 R1 "console rail": the sidebar is a dark ink rail in BOTH themes
 * (one step darker than the page in dark mode so it still reads as its own surface).
 * Ratios are enforced by src/app/tokens.contrast.test.ts: rail text ≈10:1, group headings
 * (--sidebar-muted) ≈5.4:1, brand chip (--sidebar-primary) ≈3.3:1 non-text.
 */
```

Append to the big header comment at the top of the file (before `*/`):

```css
 *
 * F15 R1 (2026-09-30): --background moved to warm sand and --card/--popover to near-white so
 * cards separate from the page without shadows; --surface-sunken added. Every text/non-text
 * pair these touch is asserted in src/app/tokens.contrast.test.ts (light + dark).
```

- [ ] **Step 4: Register Tailwind colors**

In `tailwind.config.ts` `theme.extend.colors`, add next to `border`:

```ts
        "surface-sunken": "hsl(var(--surface-sunken) / <alpha-value>)",
```

and inside the existing `sidebar: { ... }` object add:

```ts
          muted: "hsl(var(--sidebar-muted))",
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `npx vitest run src/app/tokens.contrast.test.ts`
Expected: PASS (all pairs). If a pair fails, adjust only that token's lightness and re-run.

- [ ] **Step 6: Commit**

```bash
git add src/app/globals.css tailwind.config.ts src/app/tokens.contrast.test.ts
git commit -m "feat(web): retune surface tokens and dark console rail (F15 R1)"
```

---

### Task 2: Nav model helpers

**Files:**
- Create: `src/components/console/types.ts`
- Create: `src/components/console/navModel.ts`
- Test: `src/components/console/navModel.test.ts`

**Interfaces:**
- Produces:
  ```ts
  interface ConsoleNavItem { href: string; label: string; icon: LucideIcon; badge?: ReactNode; matchPrefix?: boolean }
  interface ConsoleNavGroup { id: string; label: string; items: ConsoleNavItem[] }
  interface ConsoleNavGroupDef { id: string; label: string }
  interface ConsoleBrand { icon: LucideIcon; name: ReactNode; subtitle?: ReactNode; isLoading?: boolean }
  type GroupableNavItem = ConsoleNavItem & { group: string }
  function groupNavItems(items: GroupableNavItem[], groups: ConsoleNavGroupDef[]): ConsoleNavGroup[]
  function findActiveItem(pathname: string, groups: ConsoleNavGroup[]): { group: ConsoleNavGroup; item: ConsoleNavItem } | undefined
  ```

- [ ] **Step 1: Write the failing test**

Create `src/components/console/navModel.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { Package } from "lucide-react";
import { findActiveItem, groupNavItems } from "./navModel";
import type { ConsoleNavGroup } from "./types";

const item = (href: string, group: string, matchPrefix = true) => ({ href, label: href, icon: Package, group, matchPrefix });

describe("groupNavItems", () => {
  it("orders groups by the definition order and keeps item order", () => {
    const groups = groupNavItems(
      [item("/cod", "finance"), item("/dashboard", "ops"), item("/shipments", "ops")],
      [{ id: "ops", label: "Operations" }, { id: "finance", label: "Finance" }]
    );
    expect(groups.map((g) => g.label)).toEqual(["Operations", "Finance"]);
    expect(groups[0].items.map((i) => i.href)).toEqual(["/dashboard", "/shipments"]);
  });

  it("drops groups with no items (no orphan headings)", () => {
    const groups = groupNavItems([item("/dashboard", "ops")], [
      { id: "ops", label: "Operations" },
      { id: "admin", label: "Admin" },
    ]);
    expect(groups.map((g) => g.id)).toEqual(["ops"]);
  });

  it("returns no groups for no items", () => {
    expect(groupNavItems([], [{ id: "ops", label: "Operations" }])).toEqual([]);
  });

  it("strips the group key from items", () => {
    const [group] = groupNavItems([item("/dashboard", "ops")], [{ id: "ops", label: "Operations" }]);
    expect(group.items[0]).not.toHaveProperty("group");
  });
});

describe("findActiveItem", () => {
  const nav: ConsoleNavGroup[] = groupNavItems(
    [
      item("/dashboard", "ops"),
      item("/riders", "ops"),
      item("/riders/live-map", "ops"),
      item("/billing", "finance"),
      item("/exact-only", "finance", false),
    ],
    [{ id: "ops", label: "Operations" }, { id: "finance", label: "Finance" }]
  );

  it("matches an exact href", () => {
    expect(findActiveItem("/dashboard", nav)?.item.href).toBe("/dashboard");
  });

  it("prefers the most specific (longest) match for nested routes", () => {
    expect(findActiveItem("/riders/live-map", nav)?.item.href).toBe("/riders/live-map");
    expect(findActiveItem("/riders", nav)?.item.href).toBe("/riders");
  });

  it("matches descendants of a prefix item", () => {
    const active = findActiveItem("/billing/callback/abc", nav);
    expect(active?.item.href).toBe("/billing");
    expect(active?.group.label).toBe("Finance");
  });

  it("does not treat a shared string prefix as a descendant", () => {
    expect(findActiveItem("/ridersx", nav)).toBeUndefined();
  });

  it("does not prefix-match items without matchPrefix", () => {
    expect(findActiveItem("/exact-only/child", nav)).toBeUndefined();
  });

  it("returns undefined for an unknown path", () => {
    expect(findActiveItem("/nowhere", nav)).toBeUndefined();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/components/console/navModel.test.ts`
Expected: FAIL — cannot resolve `./navModel`.

- [ ] **Step 3: Implement**

Create `src/components/console/types.ts`:

```ts
import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

export interface ConsoleNavItem {
  href: string;
  /** Already translated by the caller. */
  label: string;
  icon: LucideIcon;
  /** Optional trailing badge; not wired to data in R1. */
  badge?: ReactNode;
  /** Active for descendants too (pathname startsWith href + "/"). */
  matchPrefix?: boolean;
}

export interface ConsoleNavGroup {
  id: string;
  label: string;
  items: ConsoleNavItem[];
}

export interface ConsoleNavGroupDef {
  id: string;
  label: string;
}

export interface ConsoleBrand {
  icon: LucideIcon;
  name: ReactNode;
  subtitle?: ReactNode;
  isLoading?: boolean;
}
```

Create `src/components/console/navModel.ts`:

```ts
import type { ConsoleNavGroup, ConsoleNavGroupDef, ConsoleNavItem } from "./types";

export type GroupableNavItem = ConsoleNavItem & { group: string };

// Buckets already permission-filtered items into their groups, in the groups' declared order.
// A group left with no items is dropped so the rail never shows an orphan heading.
export function groupNavItems(items: GroupableNavItem[], groups: ConsoleNavGroupDef[]): ConsoleNavGroup[] {
  return groups
    .map((group) => ({
      id: group.id,
      label: group.label,
      items: items
        .filter((item) => item.group === group.id)
        .map((item) => ({
          href: item.href,
          label: item.label,
          icon: item.icon,
          badge: item.badge,
          matchPrefix: item.matchPrefix,
        })),
    }))
    .filter((group) => group.items.length > 0);
}

// Longest matching href wins, so /riders/live-map highlights "Live Map" rather than "Riders".
export function findActiveItem(
  pathname: string,
  groups: ConsoleNavGroup[]
): { group: ConsoleNavGroup; item: ConsoleNavItem } | undefined {
  let best: { group: ConsoleNavGroup; item: ConsoleNavItem } | undefined;
  for (const group of groups) {
    for (const item of group.items) {
      const matches = pathname === item.href || (item.matchPrefix === true && pathname.startsWith(`${item.href}/`));
      if (matches && (!best || item.href.length > best.item.href.length)) {
        best = { group, item };
      }
    }
  }
  return best;
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `npx vitest run src/components/console/navModel.test.ts`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add src/components/console/types.ts src/components/console/navModel.ts src/components/console/navModel.test.ts
git commit -m "feat(web): add console nav model helpers (F15 R1)"
```

---

### Task 3: `ComingSoonSlot` + console message keys

**Files:**
- Create: `src/components/page/ComingSoonSlot.tsx`
- Modify: `messages/en.json`, `messages/ne.json`
- Test: `src/components/page/ComingSoonSlot.test.tsx`

**Interfaces:**
- Produces: `ComingSoonSlot({ title: string; description: string; icon?: ComponentType<LucideProps>; minHeight?: number; className?: string })` — renders a `<section aria-label={title}>` (role region) with `data-coming-soon`; message namespace `console` with keys `operationsConsole`, `needsBackend`, `notifications`, `notificationsTitle`, `notificationsDescription`, `breadcrumb`, `bsSuffix`, `branchScope`.

- [ ] **Step 1: Write the failing test**

Create `src/components/page/ComingSoonSlot.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { ComingSoonSlot } from "./ComingSoonSlot";

describe("ComingSoonSlot", () => {
  it("renders a labelled region with title, description and the needs-backend caption", () => {
    renderWithIntl(<ComingSoonSlot title="Hourly flow" description="Booked vs delivered per hour." />);

    const region = screen.getByRole("region", { name: "Hourly flow" });
    expect(region).toHaveAttribute("data-coming-soon");
    expect(screen.getByText("Booked vs delivered per hour.")).toBeInTheDocument();
    expect(screen.getByText("Needs backend")).toBeInTheDocument();
  });

  it("applies minHeight when given", () => {
    renderWithIntl(<ComingSoonSlot title="Bins" description="Sort bins." minHeight={240} />);
    expect(screen.getByRole("region", { name: "Bins" })).toHaveStyle({ minHeight: "240px" });
  });

  it("renders the caption in Nepali", () => {
    renderWithIntl(<ComingSoonSlot title="Bins" description="Sort bins." />, { locale: "ne" });
    expect(screen.getByText("ब्याकइन्ड आवश्यक")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/components/page/ComingSoonSlot.test.tsx`
Expected: FAIL — cannot resolve `./ComingSoonSlot`.

- [ ] **Step 3: Add message keys**

Add a top-level `"console"` object to `messages/en.json`:

```json
  "console": {
    "operationsConsole": "Operations console",
    "needsBackend": "Needs backend",
    "notifications": "Notifications",
    "notificationsTitle": "Notifications",
    "notificationsDescription": "Shipment exceptions, manifest alerts and settlement approvals will appear here.",
    "breadcrumb": "Breadcrumb",
    "bsSuffix": "BS",
    "branchScope": "Branch scope"
  },
```

and to `messages/ne.json`:

```json
  "console": {
    "operationsConsole": "सञ्चालन कन्सोल",
    "needsBackend": "ब्याकइन्ड आवश्यक",
    "notifications": "सूचनाहरू",
    "notificationsTitle": "सूचनाहरू",
    "notificationsDescription": "ढुवानी अपवाद, म्यानिफेस्ट सूचना र भुक्तानी स्वीकृतिहरू यहाँ देखिनेछन्।",
    "breadcrumb": "ब्रेडक्रम्ब",
    "bsSuffix": "वि.सं.",
    "branchScope": "शाखा दायरा"
  },
```

- [ ] **Step 4: Implement**

Create `src/components/page/ComingSoonSlot.tsx`:

```tsx
"use client";

import type { ComponentType } from "react";
import { Hourglass, type LucideProps } from "lucide-react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

// The one sanctioned stand-in for data the API does not serve yet (DESIGN.md No-Fake-Numbers
// Rule). `data-coming-soon` makes every slot grep-able when the backend lands.
export function ComingSoonSlot({
  title,
  description,
  icon: Icon = Hourglass,
  minHeight,
  className,
}: {
  title: string;
  description: string;
  icon?: ComponentType<LucideProps>;
  minHeight?: number;
  className?: string;
}) {
  const t = useTranslations("console");
  return (
    <section
      aria-label={title}
      data-coming-soon=""
      style={minHeight ? { minHeight } : undefined}
      className={cn(
        "flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed border-border bg-card p-6 text-center",
        className
      )}
    >
      <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-muted">
        <Icon className="h-5 w-5 text-muted-foreground" strokeWidth={1.75} aria-hidden="true" />
      </span>
      <p className="font-display text-sm font-semibold text-foreground">{title}</p>
      <p className="max-w-sm text-sm text-muted-foreground">{description}</p>
      <span className="font-mono text-[10.5px] uppercase tracking-[0.12em] text-muted-foreground">
        {t("needsBackend")}
      </span>
    </section>
  );
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx vitest run src/components/page/ComingSoonSlot.test.tsx src/i18n/messages.test.ts`
Expected: PASS (catalog parity holds).

- [ ] **Step 6: Commit**

```bash
git add src/components/page/ComingSoonSlot.tsx src/components/page/ComingSoonSlot.test.tsx messages/en.json messages/ne.json
git commit -m "feat(web): add ComingSoonSlot and console message keys (F15 R1)"
```

---

### Task 4: `formatDualDate` + `DualDate`

**Files:**
- Create: `src/components/console/formatDualDate.ts`, `src/components/console/DualDate.tsx`
- Test: `src/components/console/formatDualDate.test.ts`, `src/components/console/DualDate.test.tsx`

**Interfaces:**
- Consumes: `toBsDate(date: Date, timeZone: string): BsDate | null`, `BS_MONTHS_EN`, `BS_MONTHS_NE` from `@/lib/i18n/bsDate`; message `console.bsSuffix`.
- Produces: `CONSOLE_TIME_ZONE = "Asia/Kathmandu"`; `formatDualDate(date: Date, locale: string): { ad: string; bs: string | null }`; `DualDate({ now?: Date })`.

- [ ] **Step 1: Write the failing tests**

Create `src/components/console/formatDualDate.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "@/lib/i18n/bsDate";
import { CONSOLE_TIME_ZONE, formatDualDate } from "./formatDualDate";

describe("formatDualDate", () => {
  it("formats the AD date and the converter's BS date for the Kathmandu day", () => {
    const date = new Date("2026-09-30T06:00:00Z");
    const bs = toBsDate(date, CONSOLE_TIME_ZONE)!;
    const result = formatDualDate(date, "en");

    expect(result.ad).toContain("Wed");
    expect(result.ad).toContain("30 Sep 2026");
    expect(result.bs).toBe(`${BS_MONTHS_EN[bs.month - 1]} ${bs.day}, ${bs.year}`);
  });

  it("uses the Kathmandu calendar day near UTC midnight", () => {
    // 18:30 UTC on 30 Sep = 00:15 on 1 Oct in Kathmandu (UTC+5:45).
    const date = new Date("2026-09-30T18:30:00Z");
    const result = formatDualDate(date, "en");
    const bsKathmandu = toBsDate(date, CONSOLE_TIME_ZONE)!;
    const bsUtc = toBsDate(date, "UTC")!;

    expect(result.ad).toContain("1 Oct 2026");
    expect(bsKathmandu.day).not.toBe(bsUtc.day);
    expect(result.bs).toBe(`${BS_MONTHS_EN[bsKathmandu.month - 1]} ${bsKathmandu.day}, ${bsKathmandu.year}`);
  });

  it("uses Nepali month names for the ne locale", () => {
    const date = new Date("2026-09-30T06:00:00Z");
    const bs = toBsDate(date, CONSOLE_TIME_ZONE)!;
    expect(formatDualDate(date, "ne").bs).toBe(`${BS_MONTHS_NE[bs.month - 1]} ${bs.day}, ${bs.year}`);
  });

  it("returns bs null outside the converter's range", () => {
    expect(formatDualDate(new Date("2050-01-01T00:00:00Z"), "en").bs).toBeNull();
  });
});
```

Create `src/components/console/DualDate.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { DualDate } from "./DualDate";
import { formatDualDate } from "./formatDualDate";

describe("DualDate", () => {
  it("renders AD and BS lines with the BS suffix", () => {
    const now = new Date("2026-09-30T06:00:00Z");
    const { ad, bs } = formatDualDate(now, "en");
    renderWithIntl(<DualDate now={now} />);

    expect(screen.getByText(ad)).toBeInTheDocument();
    expect(screen.getByText(`${bs} BS`)).toBeInTheDocument();
  });

  it("renders the Nepali suffix in the ne locale", () => {
    const now = new Date("2026-09-30T06:00:00Z");
    const { bs } = formatDualDate(now, "ne");
    renderWithIntl(<DualDate now={now} />, { locale: "ne" });

    expect(screen.getByText(`${bs} वि.सं.`)).toBeInTheDocument();
  });

  it("renders only the AD line when BS is out of range", () => {
    const now = new Date("2050-01-01T00:00:00Z");
    renderWithIntl(<DualDate now={now} />);
    expect(screen.queryByText(/BS$/)).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run them to verify they fail**

Run: `npx vitest run src/components/console/formatDualDate.test.ts src/components/console/DualDate.test.tsx`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement**

Create `src/components/console/formatDualDate.ts`:

```ts
import { BS_MONTHS_EN, BS_MONTHS_NE, toBsDate } from "@/lib/i18n/bsDate";

export const CONSOLE_TIME_ZONE = "Asia/Kathmandu";

const adFormatter = new Intl.DateTimeFormat("en-GB", {
  timeZone: CONSOLE_TIME_ZONE,
  weekday: "short",
  day: "numeric",
  month: "short",
  year: "numeric",
});

// Both calendars describe the same Kathmandu calendar day — a parcel scanned at 00:15 NPT
// belongs to that day even while it is still the previous day in UTC.
export function formatDualDate(date: Date, locale: string): { ad: string; bs: string | null } {
  const ad = adFormatter.format(date);
  const bsDate = toBsDate(date, CONSOLE_TIME_ZONE);
  if (!bsDate) {
    return { ad, bs: null };
  }
  const months = locale === "ne" ? BS_MONTHS_NE : BS_MONTHS_EN;
  return { ad, bs: `${months[bsDate.month - 1]} ${bsDate.day}, ${bsDate.year}` };
}
```

Create `src/components/console/DualDate.tsx`:

```tsx
"use client";

import { useEffect, useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { formatDualDate } from "./formatDualDate";

// `now` is for tests; in the app the date is read after mount so server and client HTML
// never disagree (the server may render in another time zone or on another day).
export function DualDate({ now }: { now?: Date }) {
  const locale = useLocale();
  const t = useTranslations("console");
  const [date, setDate] = useState<Date | null>(now ?? null);

  useEffect(() => {
    if (!now) {
      setDate(new Date());
    }
  }, [now]);

  if (!date) {
    return null;
  }
  const { ad, bs } = formatDualDate(date, locale);
  return (
    <div className="hidden text-right leading-tight lg:block">
      <div className="text-[12.5px] font-medium text-foreground">{ad}</div>
      {bs && (
        <div className="text-xs text-muted-foreground">
          {bs} {t("bsSuffix")}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/components/console/formatDualDate.test.ts src/components/console/DualDate.test.tsx`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/components/console/formatDualDate.ts src/components/console/formatDualDate.test.ts src/components/console/DualDate.tsx src/components/console/DualDate.test.tsx
git commit -m "feat(web): add AD/BS dual date for the console header (F15 R1)"
```

---

### Task 5: `NotificationsButton` + `ConsoleBreadcrumb`

**Files:**
- Create: `src/components/console/NotificationsButton.tsx`, `src/components/console/ConsoleBreadcrumb.tsx`
- Test: `src/components/console/NotificationsButton.test.tsx`, `src/components/console/ConsoleBreadcrumb.test.tsx`

**Interfaces:**
- Consumes: `ComingSoonSlot` (Task 3), `findActiveItem`, `ConsoleNavGroup` (Task 2).
- Produces: `NotificationsButton()`, `ConsoleBreadcrumb({ nav: ConsoleNavGroup[] })`.

- [ ] **Step 1: Write the failing tests**

Create `src/components/console/NotificationsButton.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithIntl } from "@/test/renderWithIntl";
import { NotificationsButton } from "./NotificationsButton";

describe("NotificationsButton", () => {
  it("opens a popover holding the notifications coming-soon slot", async () => {
    const user = userEvent.setup();
    renderWithIntl(<NotificationsButton />);

    await user.click(screen.getByRole("button", { name: "Notifications" }));

    expect(await screen.findByRole("region", { name: "Notifications" })).toBeInTheDocument();
    expect(screen.getByText("Needs backend")).toBeInTheDocument();
  });
});
```

Create `src/components/console/ConsoleBreadcrumb.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { ConsoleBreadcrumb } from "./ConsoleBreadcrumb";
import type { ConsoleNavGroup } from "./types";

let pathname = "/riders/live-map";
vi.mock("next/navigation", () => ({ usePathname: () => pathname }));

const nav: ConsoleNavGroup[] = [
  {
    id: "ops",
    label: "Operations",
    items: [
      { href: "/riders", label: "Riders", icon: Package, matchPrefix: true },
      { href: "/riders/live-map", label: "Live Map", icon: Package, matchPrefix: true },
    ],
  },
];

describe("ConsoleBreadcrumb", () => {
  it("shows group › most specific page, marking the page current", () => {
    pathname = "/riders/live-map";
    renderWithIntl(<ConsoleBreadcrumb nav={nav} />);

    expect(screen.getByRole("navigation", { name: "Breadcrumb" })).toBeInTheDocument();
    expect(screen.getByText("Operations")).toBeInTheDocument();
    expect(screen.getByText("Live Map")).toHaveAttribute("aria-current", "page");
  });

  it("renders nothing for a path outside the nav", () => {
    pathname = "/elsewhere";
    const { container } = renderWithIntl(<ConsoleBreadcrumb nav={nav} />);
    expect(container).toBeEmptyDOMElement();
  });
});
```

- [ ] **Step 2: Run them to verify they fail**

Run: `npx vitest run src/components/console/NotificationsButton.test.tsx src/components/console/ConsoleBreadcrumb.test.tsx`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement**

Create `src/components/console/NotificationsButton.tsx`:

```tsx
"use client";

import { Bell } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";

// No notification feed exists yet (backend follow-up), so the bell has no unread dot and
// opens a ComingSoonSlot rather than an empty list pretending to be real.
export function NotificationsButton() {
  const t = useTranslations("console");
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="icon" className="h-10 w-10 rounded-full" aria-label={t("notifications")}>
          <Bell className="h-4 w-4" strokeWidth={1.8} aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 p-2">
        <ComingSoonSlot
          title={t("notificationsTitle")}
          description={t("notificationsDescription")}
          icon={Bell}
          className="border-0"
        />
      </PopoverContent>
    </Popover>
  );
}
```

Create `src/components/console/ConsoleBreadcrumb.tsx`:

```tsx
"use client";

import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { findActiveItem } from "./navModel";
import type { ConsoleNavGroup } from "./types";

export function ConsoleBreadcrumb({ nav }: { nav: ConsoleNavGroup[] }) {
  const pathname = usePathname();
  const t = useTranslations("console");
  const active = findActiveItem(pathname, nav);
  if (!active) {
    return null;
  }
  return (
    <Breadcrumb aria-label={t("breadcrumb")} className="hidden sm:block">
      <BreadcrumbList className="text-[13px]">
        <BreadcrumbItem>{active.group.label}</BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem>
          <BreadcrumbPage className="font-medium">{active.item.label}</BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  );
}
```

If `Breadcrumb` in `src/components/ui/breadcrumb.tsx` hard-codes `aria-label="breadcrumb"` after spreading props, move the spread after the default so the passed label wins (check the file; the shadcn original is `<nav ref={ref} aria-label="breadcrumb" {...props} />`, which already lets ours override).

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/components/console/NotificationsButton.test.tsx src/components/console/ConsoleBreadcrumb.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/components/console/NotificationsButton.tsx src/components/console/NotificationsButton.test.tsx src/components/console/ConsoleBreadcrumb.tsx src/components/console/ConsoleBreadcrumb.test.tsx
git commit -m "feat(web): add console notifications slot and breadcrumb (F15 R1)"
```

---

### Task 6: `ConsoleSidebar` + `ConsoleShell`

**Files:**
- Create: `src/components/console/ConsoleSidebar.tsx`, `src/components/console/ConsoleShell.tsx`
- Test: `src/components/console/ConsoleSidebar.test.tsx`, `src/components/console/ConsoleShell.test.tsx`

**Interfaces:**
- Consumes: Tasks 2, 4, 5 exports; shadcn `Sidebar*` primitives; `Skeleton`.
- Produces:
  ```ts
  function ConsoleSidebar(props: { brand: ConsoleBrand; scope?: ReactNode; nav: ConsoleNavGroup[]; navLoading?: boolean }): JSX.Element
  interface ConsoleShellProps { brand: ConsoleBrand; scope?: ReactNode; nav: ConsoleNavGroup[]; navLoading?: boolean; headerStart?: ReactNode; headerEnd?: ReactNode; banners?: ReactNode; children: ReactNode }
  function ConsoleShell(props: ConsoleShellProps): JSX.Element
  ```
  Test ids kept for continuity: `sidebar-brand-skeleton`, `nav-item-skeleton`.

- [ ] **Step 1: Write the failing tests**

Create `src/components/console/ConsoleSidebar.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { Package, Wallet } from "lucide-react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { SidebarProvider } from "@/components/ui/sidebar";
import { ConsoleSidebar } from "./ConsoleSidebar";
import type { ConsoleNavGroup } from "./types";

vi.mock("next/navigation", () => ({ usePathname: () => "/shipments" }));

const nav: ConsoleNavGroup[] = [
  {
    id: "ops",
    label: "Operations",
    items: [
      { href: "/dashboard", label: "Dashboard", icon: Package },
      { href: "/shipments", label: "Shipments", icon: Package, matchPrefix: true },
    ],
  },
  { id: "empty", label: "Empty Group", items: [] },
  { id: "finance", label: "Finance", items: [{ href: "/cod", label: "COD", icon: Wallet }] },
];

function renderSidebar(props: Partial<Parameters<typeof ConsoleSidebar>[0]> = {}, defaultOpen = true) {
  return renderWithIntl(
    <SidebarProvider defaultOpen={defaultOpen}>
      <ConsoleSidebar
        brand={{ icon: Package, name: "Sagarmatha Express", subtitle: "Operations console" }}
        nav={nav}
        {...props}
      />
    </SidebarProvider>
  );
}

describe("ConsoleSidebar", () => {
  it("renders brand name and subtitle", () => {
    renderSidebar();
    expect(screen.getByText("Sagarmatha Express")).toBeInTheDocument();
    expect(screen.getByText("Operations console")).toBeInTheDocument();
  });

  it("shows a brand skeleton while the brand is loading", () => {
    renderSidebar({ brand: { icon: Package, name: "X", isLoading: true } });
    expect(screen.getByTestId("sidebar-brand-skeleton")).toBeInTheDocument();
    expect(screen.queryByText("X")).not.toBeInTheDocument();
  });

  it("renders group headings and skips groups with no items", () => {
    renderSidebar();
    expect(screen.getByText("Operations")).toBeInTheDocument();
    expect(screen.getByText("Finance")).toBeInTheDocument();
    expect(screen.queryByText("Empty Group")).not.toBeInTheDocument();
  });

  it("marks only the active item with aria-current=page", () => {
    renderSidebar();
    expect(screen.getByRole("link", { name: "Shipments" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Dashboard" })).not.toHaveAttribute("aria-current");
  });

  it("renders the scope slot when expanded", () => {
    renderSidebar({ scope: <div>Scope control</div> });
    expect(screen.getByText("Scope control")).toBeInTheDocument();
  });

  it("shows nav skeleton rows instead of items while navLoading", () => {
    renderSidebar({ navLoading: true });
    expect(screen.getAllByTestId("nav-item-skeleton").length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: "Dashboard" })).not.toBeInTheDocument();
  });

  it("keeps accessible link names when collapsed to the icon rail", () => {
    renderSidebar({}, false);
    expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "COD" })).toBeInTheDocument();
  });
});
```

Create `src/components/console/ConsoleShell.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { ConsoleShell } from "./ConsoleShell";

vi.mock("next/navigation", () => ({ usePathname: () => "/dashboard" }));

describe("ConsoleShell", () => {
  it("renders brand, nav, header slots, banners and children", () => {
    renderWithIntl(
      <ConsoleShell
        brand={{ icon: Package, name: "Acme Courier" }}
        nav={[{ id: "ops", label: "Operations", items: [{ href: "/dashboard", label: "Dashboard", icon: Package }] }]}
        headerStart={<div>Search slot</div>}
        headerEnd={<div>User slot</div>}
        banners={<div>Trial banner</div>}
      >
        <p>Page body</p>
      </ConsoleShell>
    );

    expect(screen.getByText("Acme Courier")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.getByText("Search slot")).toBeInTheDocument();
    expect(screen.getByText("User slot")).toBeInTheDocument();
    expect(screen.getByText("Trial banner")).toBeInTheDocument();
    expect(screen.getByText("Page body")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Notifications" })).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Breadcrumb" })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run them to verify they fail**

Run: `npx vitest run src/components/console/ConsoleSidebar.test.tsx src/components/console/ConsoleShell.test.tsx`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement `ConsoleSidebar`**

Create `src/components/console/ConsoleSidebar.tsx`:

```tsx
"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSkeleton,
  SidebarRail,
  useSidebar,
} from "@/components/ui/sidebar";
import { Skeleton } from "@/components/ui/skeleton";
import { findActiveItem } from "./navModel";
import type { ConsoleBrand, ConsoleNavGroup } from "./types";

// Rough average of how many items a role sees — shown before the real filtered list is known.
const NAV_SKELETON_ROWS = 7;

export function ConsoleSidebar({
  brand,
  scope,
  nav,
  navLoading = false,
}: {
  brand: ConsoleBrand;
  scope?: ReactNode;
  nav: ConsoleNavGroup[];
  navLoading?: boolean;
}) {
  const pathname = usePathname();
  const { state, isMobile, setOpenMobile } = useSidebar();
  const activeHref = findActiveItem(pathname, nav)?.item.href;
  const onNavigate = isMobile ? () => setOpenMobile(false) : undefined;
  const BrandIcon = brand.icon;
  const expanded = state === "expanded";

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader className="gap-3 pt-4">
        <SidebarMenu>
          <SidebarMenuItem className="flex items-center gap-2.5 px-1 py-1">
            <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-sidebar-primary">
              <BrandIcon className="h-4 w-4 text-sidebar-primary-foreground" strokeWidth={2} aria-hidden="true" />
            </span>
            {expanded &&
              (brand.isLoading ? (
                <Skeleton className="h-4 w-28 bg-sidebar-accent" data-testid="sidebar-brand-skeleton" />
              ) : (
                <span className="flex min-w-0 flex-col">
                  <span className="truncate font-display text-[15px] font-semibold tracking-tight text-sidebar-accent-foreground">
                    {brand.name}
                  </span>
                  {brand.subtitle && <span className="truncate text-xs text-sidebar-muted">{brand.subtitle}</span>}
                </span>
              ))}
          </SidebarMenuItem>
        </SidebarMenu>
        {expanded && scope}
      </SidebarHeader>
      <SidebarContent>
        {navLoading ? (
          <SidebarGroup>
            <SidebarMenu>
              {Array.from({ length: NAV_SKELETON_ROWS }).map((_, i) => (
                <SidebarMenuItem key={i}>
                  <SidebarMenuSkeleton showIcon data-testid="nav-item-skeleton" />
                </SidebarMenuItem>
              ))}
            </SidebarMenu>
          </SidebarGroup>
        ) : (
          nav
            .filter((group) => group.items.length > 0)
            .map((group) => (
              <SidebarGroup key={group.id}>
                <SidebarGroupLabel className="font-mono text-[10.5px] uppercase tracking-[0.12em] text-sidebar-muted">
                  {group.label}
                </SidebarGroupLabel>
                <SidebarMenu>
                  {group.items.map((item) => {
                    const Icon = item.icon;
                    const active = item.href === activeHref;
                    return (
                      <SidebarMenuItem key={item.href}>
                        <SidebarMenuButton asChild isActive={active} tooltip={item.label} className="h-9 text-[13.5px]">
                          <Link href={item.href} onClick={onNavigate} aria-current={active ? "page" : undefined}>
                            <Icon strokeWidth={1.75} aria-hidden="true" />
                            <span>{item.label}</span>
                          </Link>
                        </SidebarMenuButton>
                        {item.badge && <SidebarMenuBadge>{item.badge}</SidebarMenuBadge>}
                      </SidebarMenuItem>
                    );
                  })}
                </SidebarMenu>
              </SidebarGroup>
            ))
        )}
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
```

- [ ] **Step 4: Implement `ConsoleShell`**

Create `src/components/console/ConsoleShell.tsx`:

```tsx
"use client";

import type { ReactNode } from "react";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { ConsoleBreadcrumb } from "./ConsoleBreadcrumb";
import { ConsoleSidebar } from "./ConsoleSidebar";
import { DualDate } from "./DualDate";
import { NotificationsButton } from "./NotificationsButton";
import type { ConsoleBrand, ConsoleNavGroup } from "./types";

export interface ConsoleShellProps {
  brand: ConsoleBrand;
  scope?: ReactNode;
  /** Already permission-filtered by the calling surface. */
  nav: ConsoleNavGroup[];
  navLoading?: boolean;
  headerStart?: ReactNode;
  headerEnd?: ReactNode;
  banners?: ReactNode;
  children: ReactNode;
}

// The one shell for the staff, merchant and platform consoles (F15 R1). Surfaces differ only in
// what they pass in; the frame, rail and header are identical so they cannot drift apart.
export function ConsoleShell({ brand, scope, nav, navLoading, headerStart, headerEnd, banners, children }: ConsoleShellProps) {
  return (
    <SidebarProvider>
      <ConsoleSidebar brand={brand} scope={scope} nav={nav} navLoading={navLoading} />
      <SidebarInset>
        {banners}
        <header className="sticky top-0 z-10 flex h-16 shrink-0 items-center gap-3 border-b border-border bg-card px-4 sm:px-6">
          <SidebarTrigger className="h-9 w-9 rounded-lg border border-input" />
          <ConsoleBreadcrumb nav={nav} />
          {headerStart}
          <div className="ml-auto flex items-center gap-2 sm:gap-3">
            <DualDate />
            <NotificationsButton />
            {headerEnd}
          </div>
        </header>
        <div className="min-w-0 flex-1 bg-background p-4 sm:p-6 lg:p-8">{children}</div>
      </SidebarInset>
    </SidebarProvider>
  );
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx vitest run src/components/console`
Expected: PASS (all console tests).

- [ ] **Step 6: Commit**

```bash
git add src/components/console/ConsoleSidebar.tsx src/components/console/ConsoleSidebar.test.tsx src/components/console/ConsoleShell.tsx src/components/console/ConsoleShell.test.tsx
git commit -m "feat(web): add shared ConsoleShell with dark grouped rail (F15 R1)"
```

---

### Task 7: Staff console wiring

**Files:**
- Modify: `src/lib/navigation/navItems.ts`
- Create: `src/components/layout/useStaffNavGroups.ts`
- Test: `src/components/layout/useStaffNavGroups.test.tsx`
- Modify: `src/components/layout/AppShell.tsx`, `src/components/layout/BranchSwitcher.tsx`
- Modify: `messages/en.json`, `messages/ne.json` (`nav.groups`, remove `nav.group`)
- Delete: `src/components/app-sidebar.tsx`, `src/components/app-sidebar.test.tsx`, `src/components/nav-main.tsx`, `src/components/nav-main.test.tsx`

**Interfaces:**
- Consumes: `groupNavItems` (Task 2), `ConsoleShell` (Task 6), `useSession`.
- Produces:
  ```ts
  type StaffNavGroup = "operations" | "finance" | "network" | "admin";
  interface NavItem { href; labelKey; permission: string | string[]; icon; group: StaffNavGroup }  // group added
  const STAFF_NAV_GROUPS: StaffNavGroup[];
  function hasAnyPermission(granted: string[], required: string | string[]): boolean;
  function useStaffNavGroups(): { groups: ConsoleNavGroup[]; isLoading: boolean };
  ```

- [ ] **Step 1: Write the failing test**

Create `src/components/layout/useStaffNavGroups.test.tsx`:

```tsx
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { IntlTestProvider } from "@/test/renderWithIntl";
import { useStaffNavGroups } from "./useStaffNavGroups";

function mockSession(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u1",
      organizationId: "o1",
      organizationName: "Acme",
      email: "a@b.com",
      fullName: "A B",
      permissions,
      hasAllBranchesScope: true,
      branches: [],
    }),
  }) as unknown as typeof fetch;
}

function wrapper(locale: "en" | "ne" = "en") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <IntlTestProvider locale={locale}>{children}</IntlTestProvider>
      </QueryClientProvider>
    );
  };
}

const labels = (groups: { label: string; items: { label: string }[] }[]) =>
  groups.map((g) => [g.label, g.items.map((i) => i.label)]);

describe("useStaffNavGroups", () => {
  it("groups permitted items under their headings in order", async () => {
    mockSession(["report.read", "shipment.read", "cod.read", "branch.read", "audit.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([
      ["Operations", ["Dashboard", "Shipments"]],
      ["Finance", ["COD"]],
      ["Network", ["Branches"]],
      ["Admin", ["Audit Logs"]],
    ]);
  });

  it("omits groups whose items are all hidden", async () => {
    mockSession(["report.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.groups.map((g) => g.label)).toEqual(["Operations"]);
  });

  it("returns no groups for a session with no permissions", async () => {
    mockSession([]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.groups).toEqual([]);
  });

  it("shows Admin when the session has either role.manage or user.read", async () => {
    mockSession(["user.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([["Admin", ["Admin"]]]);
  });

  it("uses prefix matching so nested routes resolve", async () => {
    mockSession(["rider.read", "rider.location.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.groups[0].items.every((i) => i.matchPrefix)).toBe(true);
  });

  it("translates headings and labels in Nepali", async () => {
    mockSession(["report.read", "shipment.read"]);
    const { result } = renderHook(() => useStaffNavGroups(), { wrapper: wrapper("ne") });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([["सञ्चालन", ["ड्यासबोर्ड", "ढुवानीहरू"]]]);
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/components/layout/useStaffNavGroups.test.tsx`
Expected: FAIL — cannot resolve `./useStaffNavGroups`.

- [ ] **Step 3: Extend the staff nav catalog**

In `src/lib/navigation/navItems.ts`, add after the `NavLabelKey` type:

```ts
export type StaffNavGroup = "operations" | "finance" | "network" | "admin";

// Order of the section headings in the console rail (F15 R1).
export const STAFF_NAV_GROUPS: StaffNavGroup[] = ["operations", "finance", "network", "admin"];

export function hasAnyPermission(granted: string[], required: string | string[]): boolean {
  const needed = Array.isArray(required) ? required : [required];
  return needed.some((permission) => granted.includes(permission));
}
```

Add `group: StaffNavGroup;` to `NavItem`, and replace `NAV_ITEMS` with (order = rail order):

```ts
export const NAV_ITEMS: NavItem[] = [
  { href: "/dashboard", labelKey: "dashboard", permission: "report.read", icon: LayoutDashboard, group: "operations" },
  { href: "/shipments", labelKey: "shipments", permission: "shipment.read", icon: Package, group: "operations" },
  { href: "/manifests", labelKey: "manifests", permission: "manifest.read", icon: Truck, group: "operations" },
  { href: "/riders", labelKey: "riders", permission: "rider.read", icon: Bike, group: "operations" },
  { href: "/riders/live-map", labelKey: "liveMap", permission: "rider.location.read", icon: MapPin, group: "operations" },
  { href: "/ndrs", labelKey: "ndrs", permission: "ndr.read", icon: AlertTriangle, group: "operations" },
  { href: "/return-shipments", labelKey: "returnShipments", permission: "rto.read", icon: Undo2, group: "operations" },
  { href: "/rider/tasks", labelKey: "myDeliveries", permission: "delivery.attempt", icon: ClipboardCheck, group: "operations" },
  { href: "/cod", labelKey: "cod", permission: "cod.read", icon: Wallet, group: "finance" },
  { href: "/billing", labelKey: "billing", permission: "org.billing.manage", icon: CreditCard, group: "finance" },
  { href: "/branches", labelKey: "branches", permission: "branch.read", icon: Building2, group: "network" },
  { href: "/admin", labelKey: "admin", permission: ["role.manage", "user.read"], icon: ShieldCheck, group: "admin" },
  { href: "/audit-logs", labelKey: "auditLogs", permission: "audit.read", icon: History, group: "admin" },
];
```

- [ ] **Step 4: Message keys**

In `messages/en.json` `nav`: delete `"group": "Platform"` and add

```json
    "groups": { "operations": "Operations", "finance": "Finance", "network": "Network", "admin": "Admin" },
```

In `messages/ne.json` `nav`: delete `"group": "प्लेटफर्म"` and add

```json
    "groups": { "operations": "सञ्चालन", "finance": "वित्त", "network": "नेटवर्क", "admin": "प्रशासन" },
```

Run `grep -rn '"group")\|t("group")' src` — the only user was `nav-main.tsx` (deleted in Step 7).

- [ ] **Step 5: Implement the hook**

Create `src/components/layout/useStaffNavGroups.ts`:

```ts
"use client";

import { useTranslations } from "next-intl";
import { useSession } from "@/features/auth/hooks/useSession";
import { NAV_ITEMS, STAFF_NAV_GROUPS, hasAnyPermission } from "@/lib/navigation/navItems";
import { groupNavItems } from "@/components/console/navModel";
import type { ConsoleNavGroup } from "@/components/console/types";

// Same visibility rule as usePermission without a branch filter: an item shows when the session
// holds any of its permissions. Hidden items are omitted entirely, never disabled.
export function useStaffNavGroups(): { groups: ConsoleNavGroup[]; isLoading: boolean } {
  const t = useTranslations("nav");
  const { data: session, isLoading } = useSession();
  const granted = session?.permissions ?? [];

  const items = NAV_ITEMS.filter((item) => hasAnyPermission(granted, item.permission)).map((item) => ({
    href: item.href,
    label: t(item.labelKey),
    icon: item.icon,
    group: item.group,
    matchPrefix: true,
  }));

  const groups = groupNavItems(
    items,
    STAFF_NAV_GROUPS.map((id) => ({ id, label: t(`groups.${id}`) }))
  );
  return { groups, isLoading };
}
```

- [ ] **Step 6: Rewire `AppShell` and restyle `BranchSwitcher`**

Replace `src/components/layout/AppShell.tsx` with:

```tsx
"use client";

import type { ReactNode } from "react";
import { Package } from "lucide-react";
import { useTranslations } from "next-intl";
import { ConsoleShell } from "@/components/console/ConsoleShell";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { CommandPalette } from "@/components/layout/CommandPalette";
import { HeaderSearch } from "@/components/layout/HeaderSearch";
import { ImpersonationBanner } from "@/components/layout/ImpersonationBanner";
import { LanguageSwitcher } from "@/components/layout/LanguageSwitcher";
import { LocaleSync } from "@/components/layout/LocaleSync";
import { ReauthDialog } from "@/components/layout/ReauthDialog";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { TrialStatusBanner } from "@/components/layout/TrialStatusBanner";
import { UserMenu } from "@/components/layout/UserMenu";
import { useStaffNavGroups } from "@/components/layout/useStaffNavGroups";
import { Separator } from "@/components/ui/separator";
import { useSession } from "@/features/auth/hooks/useSession";

export function AppShell({ children }: { children: ReactNode }) {
  const tCommon = useTranslations("common");
  const tConsole = useTranslations("console");
  const { data: session, isLoading } = useSession();
  const { groups, isLoading: navLoading } = useStaffNavGroups();

  return (
    <>
      <LocaleSync />
      <ConsoleShell
        brand={{
          icon: Package,
          name: session?.organizationName ?? tCommon("appName"),
          subtitle: tConsole("operationsConsole"),
          isLoading,
        }}
        scope={isLoading ? undefined : <BranchSwitcher />}
        nav={groups}
        navLoading={navLoading}
        headerStart={<HeaderSearch />}
        headerEnd={
          <>
            <LanguageSwitcher />
            <ThemeToggle />
            <Separator orientation="vertical" className="h-6" />
            <UserMenu />
          </>
        }
        banners={
          <>
            <ImpersonationBanner />
            <TrialStatusBanner />
          </>
        }
      >
        {children}
      </ConsoleShell>
      <ReauthDialog />
      <CommandPalette />
    </>
  );
}
```

In `src/components/layout/BranchSwitcher.tsx`, add `import { useTranslations } from "next-intl";`, call `const t = useTranslations("console");` at the top of the component (before any early return — hooks order), and replace the `DropdownMenuTrigger` child with:

```tsx
        <Button
          variant="ghost"
          className="h-auto min-h-12 w-full justify-start gap-2.5 rounded-lg border border-sidebar-border bg-sidebar-accent/60 px-3 py-2 text-left text-sidebar-accent-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground focus-visible:ring-sidebar-ring"
        >
          <Building2 className="h-4 w-4 shrink-0 text-sidebar-primary" strokeWidth={2} aria-hidden="true" />
          <span className="flex min-w-0 flex-1 flex-col">
            <span className="text-[11px] font-normal text-sidebar-muted">{t("branchScope")}</span>
            <span className="truncate text-[13px] font-medium">{active?.branchName ?? "Select branch"}</span>
          </span>
          <ChevronDown className="h-3.5 w-3.5 shrink-0 text-sidebar-muted" strokeWidth={2} aria-hidden="true" />
        </Button>
```

Before moving on, run `grep -n "useSidebar" src/components/layout/ReauthDialog.tsx src/components/layout/CommandPalette.tsx` — both now render outside `SidebarProvider`; expected: no matches. If either uses `useSidebar`, move it inside by passing it through `ConsoleShell`'s `banners` slot instead.

- [ ] **Step 7: Delete the superseded sidebar files**

```bash
git rm src/components/app-sidebar.tsx src/components/app-sidebar.test.tsx src/components/nav-main.tsx src/components/nav-main.test.tsx
```

Run: `grep -rn "app-sidebar\|nav-main\|NavMain\b\|AppSidebar" src` — expected: no matches (merchant files are handled in Task 8; `MerchantNavMain` is a different name and may still match — ignore it here).

- [ ] **Step 8: Run tests and type-check**

Run: `npx vitest run src/components/layout src/components/console src/i18n src/components/layout/CommandPalette.test.tsx && npx tsc --noEmit`
Expected: PASS; tsc no errors. `CommandPalette.test.tsx` must still pass unchanged (it proves `NAV_ITEMS` is still the palette's single source).

- [ ] **Step 9: Commit**

```bash
git add -A src/lib/navigation/navItems.ts src/components/layout messages/en.json messages/ne.json src/components/app-sidebar.tsx src/components/app-sidebar.test.tsx src/components/nav-main.tsx src/components/nav-main.test.tsx
git commit -m "feat(web): move staff console onto ConsoleShell with grouped nav (F15 R1)"
```

---

### Task 8: Merchant console wiring

**Files:**
- Create: `src/components/merchant-layout/merchantNav.ts`, `src/components/merchant-layout/useMerchantNavGroups.ts`
- Test: `src/components/merchant-layout/useMerchantNavGroups.test.tsx`
- Modify: `src/components/merchant-layout/MerchantAppShell.tsx`
- Modify: `src/app/merchant/api-keys/page.tsx:17`, `src/app/merchant/staff/page.tsx:16` (comment text only)
- Delete: `src/components/merchant-layout/MerchantAppSidebar.tsx`, `MerchantNavMain.tsx`, `MerchantNavMain.test.tsx`

**Interfaces:**
- Consumes: `groupNavItems`, `ConsoleShell`, `useMerchantSession`.
- Produces: `MERCHANT_NAV_ITEMS`, `MERCHANT_NAV_GROUPS`, `useMerchantNavGroups(): { groups: ConsoleNavGroup[]; isLoading: boolean }`.

- [ ] **Step 1: Write the failing test**

Create `src/components/merchant-layout/useMerchantNavGroups.test.tsx`:

```tsx
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useMerchantNavGroups } from "./useMerchantNavGroups";

function mockSession(permissions: string[]) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      merchantUserId: "mu1",
      merchantId: "m1",
      organizationId: "o1",
      merchantDisplayName: "Acme Traders",
      email: "owner@acme.test",
      fullName: "Owner Person",
      role: "Owner",
      permissions,
      kycStatus: "Pending",
    }),
  }) as unknown as typeof fetch;
}

function wrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  };
}

const labels = (groups: { label: string; items: { label: string }[] }[]) =>
  groups.map((g) => [g.label, g.items.map((i) => i.label)]);

describe("useMerchantNavGroups", () => {
  it("always shows Dashboard, even with no permissions", async () => {
    mockSession([]);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([["Overview", ["Dashboard"]]]);
  });

  it("hides Staff and API Keys for a Staff-role session", async () => {
    mockSession(["shipment.read", "merchant.pickups.read", "merchant.cod.read", "merchant.kyc.upload"]);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([
      ["Overview", ["Dashboard"]],
      ["Shipping", ["Shipments", "Pickups"]],
      ["Money", ["COD & Invoices"]],
      ["Account", ["KYC"]],
    ]);
  });

  it("shows Staff and API Keys for an Owner-role session", async () => {
    mockSession(["merchant.staff.manage", "merchant.apikeys.manage"]);
    const { result } = renderHook(() => useMerchantNavGroups(), { wrapper: wrapper() });

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(labels(result.current.groups)).toEqual([
      ["Overview", ["Dashboard"]],
      ["Account", ["Staff", "API Keys"]],
    ]);
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npx vitest run src/components/merchant-layout/useMerchantNavGroups.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement catalog + hook**

Create `src/components/merchant-layout/merchantNav.ts`:

```ts
import { KeyRound, LayoutDashboard, Package, ShieldCheck, Truck, Users, Wallet, type LucideIcon } from "lucide-react";

export type MerchantNavGroup = "overview" | "shipping" | "money" | "account";

export interface MerchantNavItem {
  href: string;
  label: string;
  /** Omitted means always visible — only Dashboard has no permission gate. */
  permission?: string;
  icon: LucideIcon;
  group: MerchantNavGroup;
}

export const MERCHANT_NAV_GROUPS: { id: MerchantNavGroup; label: string }[] = [
  { id: "overview", label: "Overview" },
  { id: "shipping", label: "Shipping" },
  { id: "money", label: "Money" },
  { id: "account", label: "Account" },
];

export const MERCHANT_NAV_ITEMS: MerchantNavItem[] = [
  { href: "/merchant/dashboard", label: "Dashboard", icon: LayoutDashboard, group: "overview" },
  { href: "/merchant/shipments", label: "Shipments", permission: "shipment.read", icon: Package, group: "shipping" },
  { href: "/merchant/pickups", label: "Pickups", permission: "merchant.pickups.read", icon: Truck, group: "shipping" },
  { href: "/merchant/cod", label: "COD & Invoices", permission: "merchant.cod.read", icon: Wallet, group: "money" },
  { href: "/merchant/kyc", label: "KYC", permission: "merchant.kyc.upload", icon: ShieldCheck, group: "account" },
  { href: "/merchant/staff", label: "Staff", permission: "merchant.staff.manage", icon: Users, group: "account" },
  { href: "/merchant/api-keys", label: "API Keys", permission: "merchant.apikeys.manage", icon: KeyRound, group: "account" },
];
```

Create `src/components/merchant-layout/useMerchantNavGroups.ts`:

```ts
"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import { groupNavItems } from "@/components/console/navModel";
import type { ConsoleNavGroup } from "@/components/console/types";
import { MERCHANT_NAV_GROUPS, MERCHANT_NAV_ITEMS } from "./merchantNav";

export function useMerchantNavGroups(): { groups: ConsoleNavGroup[]; isLoading: boolean } {
  const { data: session, isLoading } = useMerchantSession();
  const granted = session?.permissions ?? [];
  const items = MERCHANT_NAV_ITEMS.filter((item) => !item.permission || granted.includes(item.permission)).map(
    (item) => ({ href: item.href, label: item.label, icon: item.icon, group: item.group, matchPrefix: true })
  );
  return { groups: groupNavItems(items, MERCHANT_NAV_GROUPS), isLoading };
}
```

- [ ] **Step 4: Rewire the shell**

Replace `src/components/merchant-layout/MerchantAppShell.tsx` with:

```tsx
"use client";

import type { ReactNode } from "react";
import { Store } from "lucide-react";
import { ConsoleShell } from "@/components/console/ConsoleShell";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { MerchantUserMenu } from "@/components/merchant-layout/MerchantUserMenu";
import { useMerchantNavGroups } from "@/components/merchant-layout/useMerchantNavGroups";
import { Separator } from "@/components/ui/separator";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";

// Merchants have no branch concept (no scope slot) and no staff search index (no headerStart);
// ReauthDialog/CommandPalette stay staff-only.
export function MerchantAppShell({ children }: { children: ReactNode }) {
  const { data: session, isLoading } = useMerchantSession();
  const { groups, isLoading: navLoading } = useMerchantNavGroups();
  return (
    <ConsoleShell
      brand={{ icon: Store, name: session?.merchantDisplayName ?? "Merchant Portal", subtitle: "Merchant portal", isLoading }}
      nav={groups}
      navLoading={navLoading}
      headerEnd={
        <>
          <ThemeToggle />
          <Separator orientation="vertical" className="h-6" />
          <MerchantUserMenu />
        </>
      }
    >
      {children}
    </ConsoleShell>
  );
}
```

- [ ] **Step 5: Delete superseded files and fix comments**

```bash
git rm src/components/merchant-layout/MerchantAppSidebar.tsx src/components/merchant-layout/MerchantNavMain.tsx src/components/merchant-layout/MerchantNavMain.test.tsx
```

In `src/app/merchant/api-keys/page.tsx` and `src/app/merchant/staff/page.tsx`, replace the text `The nav entry (MerchantNavMain)` with `The nav entry (merchantNav.ts)`.

Run: `grep -rn "MerchantNavMain\|MerchantAppSidebar" src` — expected: no matches.

- [ ] **Step 6: Run tests and type-check**

Run: `npx vitest run src/components/merchant-layout src/app/merchant && npx tsc --noEmit`
Expected: PASS; no type errors.

- [ ] **Step 7: Commit**

```bash
git add -A src/components/merchant-layout src/app/merchant/api-keys/page.tsx src/app/merchant/staff/page.tsx
git commit -m "feat(web): move merchant portal onto ConsoleShell (F15 R1)"
```

---

### Task 9: Platform console wiring

**Files:**
- Create: `src/features/platform/platformNav.ts`, `src/features/platform/PlatformUserMenu.tsx`, `src/features/platform/PlatformShell.tsx`
- Test: `src/features/platform/platformNav.test.ts`, `src/features/platform/PlatformUserMenu.test.tsx`
- Modify: `src/app/platform/layout.tsx`
- Delete: `src/features/platform/PlatformNav.tsx`, `src/features/platform/PlatformNav.test.tsx`

**Interfaces:**
- Consumes: `groupNavItems`, `findActiveItem`, `ConsoleShell`.
- Produces: `PLATFORM_NAV: ConsoleNavGroup[]` (static — platform has no permission filtering), `PlatformUserMenu()`, `PlatformShell({ children })`.

- [ ] **Step 1: Write the failing tests**

Create `src/features/platform/platformNav.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { findActiveItem } from "@/components/console/navModel";
import { PLATFORM_NAV } from "./platformNav";

describe("PLATFORM_NAV", () => {
  it("groups every platform page under Tenants, Reference and System", () => {
    expect(PLATFORM_NAV.map((g) => [g.label, g.items.map((i) => i.label)])).toEqual([
      ["Tenants", ["Dashboard", "Organizations", "Plans", "Invoices"]],
      ["Reference", ["Countries"]],
      ["System", ["Jobs", "Queues", "Audit Logs", "Settings"]],
    ]);
  });

  it("highlights a section for its nested routes", () => {
    expect(findActiveItem("/platform/organizations/abc", PLATFORM_NAV)?.item.label).toBe("Organizations");
    expect(findActiveItem("/platform/plans", PLATFORM_NAV)?.item.label).toBe("Plans");
  });
});
```

Create `src/features/platform/PlatformUserMenu.test.tsx`:

```tsx
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

import { PlatformUserMenu } from "./PlatformUserMenu";

describe("PlatformUserMenu", () => {
  beforeEach(() => {
    push.mockClear();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true }));
  });

  it("signs out through the platform logout endpoint and returns to /platform-login", async () => {
    const user = userEvent.setup();
    render(<PlatformUserMenu />);

    await user.click(screen.getByRole("button", { name: /platform admin/i }));
    await user.click(await screen.findByRole("menuitem", { name: /sign out/i }));

    expect(fetch).toHaveBeenCalledWith("/api/platform-auth/logout", { method: "POST" });
    expect(push).toHaveBeenCalledWith("/platform-login");
  });
});
```

- [ ] **Step 2: Run them to verify they fail**

Run: `npx vitest run src/features/platform/platformNav.test.ts src/features/platform/PlatformUserMenu.test.tsx`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement**

Create `src/features/platform/platformNav.ts`:

```ts
import {
  Building2,
  Cog,
  FileText,
  Globe,
  History,
  LayoutDashboard,
  ListChecks,
  Layers,
  Server,
} from "lucide-react";
import { groupNavItems } from "@/components/console/navModel";

// Platform operators (SuperAdmin) see every entry — no permission filtering at this layer,
// same as the previous top-bar PlatformNav.
export const PLATFORM_NAV = groupNavItems(
  [
    { href: "/platform/dashboard", label: "Dashboard", icon: LayoutDashboard, group: "tenants", matchPrefix: true },
    { href: "/platform/organizations", label: "Organizations", icon: Building2, group: "tenants", matchPrefix: true },
    { href: "/platform/plans", label: "Plans", icon: Layers, group: "tenants", matchPrefix: true },
    { href: "/platform/invoices", label: "Invoices", icon: FileText, group: "tenants", matchPrefix: true },
    { href: "/platform/countries", label: "Countries", icon: Globe, group: "reference", matchPrefix: true },
    { href: "/platform/jobs", label: "Jobs", icon: ListChecks, group: "system", matchPrefix: true },
    { href: "/platform/queues", label: "Queues", icon: Server, group: "system", matchPrefix: true },
    { href: "/platform/audit-logs", label: "Audit Logs", icon: History, group: "system", matchPrefix: true },
    { href: "/platform/settings", label: "Settings", icon: Cog, group: "system", matchPrefix: true },
  ],
  [
    { id: "tenants", label: "Tenants" },
    { id: "reference", label: "Reference" },
    { id: "system", label: "System" },
  ]
);
```

(If any of `Cog`, `ListChecks`, `Layers` is missing from the installed lucide version, `tsc` fails — substitute `Settings`, `ClipboardList`, `Package` respectively.)

Create `src/features/platform/PlatformUserMenu.tsx`:

```tsx
"use client";

import { useRouter } from "next/navigation";
import { ChevronDown, LogOut, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export function PlatformUserMenu() {
  const router = useRouter();

  async function handleSignOut() {
    await fetch("/api/platform-auth/logout", { method: "POST" });
    router.push("/platform-login");
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="gap-2 rounded-full px-2" aria-label="Platform admin">
          <ShieldCheck className="h-4 w-4" strokeWidth={2} aria-hidden="true" />
          <span className="hidden text-sm sm:inline" aria-hidden="true">Platform admin</span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" strokeWidth={2} aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-48">
        <DropdownMenuLabel>Platform admin</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={handleSignOut}>
          <LogOut className="mr-2 h-4 w-4" strokeWidth={2} aria-hidden="true" />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

Create `src/features/platform/PlatformShell.tsx`:

```tsx
"use client";

import type { ReactNode } from "react";
import { ShieldCheck } from "lucide-react";
import { ConsoleShell } from "@/components/console/ConsoleShell";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { Separator } from "@/components/ui/separator";
import { PLATFORM_NAV } from "./platformNav";
import { PlatformUserMenu } from "./PlatformUserMenu";

export function PlatformShell({ children }: { children: ReactNode }) {
  return (
    <ConsoleShell
      brand={{ icon: ShieldCheck, name: "Platform Console", subtitle: "SaaS operator" }}
      nav={PLATFORM_NAV}
      headerEnd={
        <>
          <ThemeToggle />
          <Separator orientation="vertical" className="h-6" />
          <PlatformUserMenu />
        </>
      }
    >
      {children}
    </ConsoleShell>
  );
}
```

Replace `src/app/platform/layout.tsx` with:

```tsx
import { PlatformShell } from "@/features/platform/PlatformShell";

export default function PlatformLayout({ children }: { children: React.ReactNode }) {
  return <PlatformShell>{children}</PlatformShell>;
}
```

- [ ] **Step 4: Delete the old top bar**

```bash
git rm src/features/platform/PlatformNav.tsx src/features/platform/PlatformNav.test.tsx
```

Run: `grep -rn "PlatformNav\b" src` — expected: no matches.

- [ ] **Step 5: Run tests and type-check**

Run: `npx vitest run src/features/platform src/app/platform && npx tsc --noEmit`
Expected: PASS; no type errors. Platform page tests render pages without the layout, so they are unaffected; any that fail on missing `SidebarProvider` indicate they rendered the layout — wrap them with `SidebarProvider` rather than weakening assertions.

- [ ] **Step 6: Commit**

```bash
git add -A src/features/platform src/app/platform/layout.tsx
git commit -m "feat(web): move platform console onto ConsoleShell (F15 R1)"
```

---

### Task 10: Page primitives + dashboard header adoption

**Files:**
- Create: `src/components/page/PageHeader.tsx`, `SectionCard.tsx`, `KpiTile.tsx`, `PipelineStrip.tsx`, `SheetHeaderBlock.tsx`
- Test: `src/components/page/page.test.tsx`
- Modify: `src/components/states/StatTile.tsx`, `src/features/reports/components/DashboardHeader.tsx`

**Interfaces:**
- Produces:
  ```ts
  PageHeader({ title: ReactNode; description?: ReactNode; eyebrow?: ReactNode; badge?: ReactNode; actions?: ReactNode })
  SectionCard({ title: string; icon?: ComponentType<LucideProps>; description?: ReactNode; action?: ReactNode; className?: string; children: ReactNode })
  type KpiDeltaTone = "positive" | "negative" | "neutral"
  KpiTile({ label: string; value: ReactNode; delta?: { text: string; tone: KpiDeltaTone }; note?: ReactNode })
  interface PipelineStage { key: string; label: string; count: number; token: string; share?: number; caption?: ReactNode; href?: string }
  PipelineStrip({ stages: PipelineStage[]; ariaLabel: string })
  SheetHeaderBlock({ eyebrow: string; title: ReactNode; mono?: boolean; status?: ReactNode; facts?: { label: string; value: ReactNode }[] })  // must render inside a Sheet
  ```

- [ ] **Step 1: Write the failing tests**

Create `src/components/page/page.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { Sheet, SheetContent, SheetDescription } from "@/components/ui/sheet";
import { PageHeader } from "./PageHeader";
import { SectionCard } from "./SectionCard";
import { KpiTile } from "./KpiTile";
import { PipelineStrip } from "./PipelineStrip";
import { SheetHeaderBlock } from "./SheetHeaderBlock";

describe("PageHeader", () => {
  it("renders an h1 title with eyebrow, description, badge and actions", () => {
    render(
      <PageHeader
        eyebrow="Good morning, Anita"
        title="Shipments"
        description="2,418 active parcels"
        badge={<span>Beta</span>}
        actions={<button type="button">Book shipment</button>}
      />
    );
    expect(screen.getByRole("heading", { level: 1, name: "Shipments" })).toBeInTheDocument();
    expect(screen.getByText("Good morning, Anita")).toBeInTheDocument();
    expect(screen.getByText("2,418 active parcels")).toBeInTheDocument();
    expect(screen.getByText("Beta")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Book shipment" })).toBeInTheDocument();
  });
});

describe("SectionCard", () => {
  it("renders an h2 title, description, action and body", () => {
    render(
      <SectionCard title="Branch performance" icon={Package} description="Last 30 days" action={<a href="/branches">All</a>}>
        <p>Body</p>
      </SectionCard>
    );
    expect(screen.getByRole("heading", { level: 2, name: "Branch performance" })).toBeInTheDocument();
    expect(screen.getByText("Last 30 days")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "All" })).toBeInTheDocument();
    expect(screen.getByText("Body")).toBeInTheDocument();
  });
});

describe("KpiTile", () => {
  it("renders label, value, delta text with its tone, and note", () => {
    render(<KpiTile label="NDR rate" value="5.2%" delta={{ text: "+0.4 pts", tone: "negative" }} note="vs last 7 days" />);
    expect(screen.getByText("NDR rate")).toBeInTheDocument();
    expect(screen.getByText("5.2%")).toBeInTheDocument();
    const delta = screen.getByText("+0.4 pts");
    expect(delta).toHaveAttribute("data-tone", "negative");
    expect(screen.getByText("vs last 7 days")).toBeInTheDocument();
  });

  it("renders without a delta", () => {
    render(<KpiTile label="Riders" value={37} />);
    expect(screen.getByText("37")).toBeInTheDocument();
    expect(document.querySelector("[data-tone]")).toBeNull();
  });
});

describe("PipelineStrip", () => {
  const stages = [
    { key: "booked", label: "Booked", count: 1284, token: "status-created", share: 1.4, href: "/shipments?status=Created" },
    { key: "delivered", label: "Delivered", count: 0, token: "status-delivered", caption: "Today" },
  ];

  it("renders a labelled list with formatted counts and links only where href is given", () => {
    render(<PipelineStrip stages={stages} ariaLabel="Parcel pipeline" />);
    expect(screen.getByRole("list", { name: "Parcel pipeline" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Booked/ })).toHaveAttribute("href", "/shipments?status=Created");
    expect(screen.getByText("1,284")).toBeInTheDocument();
    expect(screen.getByText("0")).toBeInTheDocument();
    expect(screen.getAllByRole("link")).toHaveLength(1);
  });

  it("clamps share bars to 0–100%", () => {
    render(<PipelineStrip stages={stages} ariaLabel="Parcel pipeline" />);
    const bar = document.querySelector('[data-share-bar="booked"]') as HTMLElement;
    expect(bar.style.width).toBe("100%");
  });
});

describe("SheetHeaderBlock", () => {
  it("renders eyebrow, the sheet title, status and facts", () => {
    render(
      <Sheet open>
        <SheetContent>
          <SheetHeaderBlock
            eyebrow="AWB · EXPRESS · COD"
            title="NX-260930-00148"
            mono
            status={<span>Out for delivery</span>}
            facts={[{ label: "COD", value: "Rs 2,500" }]}
          />
          <SheetDescription>detail</SheetDescription>
        </SheetContent>
      </Sheet>
    );
    expect(screen.getByRole("dialog", { name: "NX-260930-00148" })).toBeInTheDocument();
    expect(screen.getByText("AWB · EXPRESS · COD")).toBeInTheDocument();
    expect(screen.getByText("Out for delivery")).toBeInTheDocument();
    expect(screen.getByText("COD")).toBeInTheDocument();
    expect(screen.getByText("Rs 2,500")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/components/page/page.test.tsx`
Expected: FAIL — modules not found.

- [ ] **Step 3: Implement the primitives**

Create `src/components/page/PageHeader.tsx`:

```tsx
import type { ReactNode } from "react";

export function PageHeader({
  title,
  description,
  eyebrow,
  badge,
  actions,
}: {
  title: ReactNode;
  description?: ReactNode;
  eyebrow?: ReactNode;
  badge?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0 space-y-1.5">
        {eyebrow && <p className="text-sm text-muted-foreground">{eyebrow}</p>}
        <div className="flex flex-wrap items-center gap-2.5">
          <h1 className="font-display text-2xl font-semibold tracking-tight text-foreground sm:text-[26px]">{title}</h1>
          {badge}
        </div>
        {description && <p className="text-[13px] text-muted-foreground">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2.5">{actions}</div>}
    </div>
  );
}
```

Create `src/components/page/SectionCard.tsx`:

```tsx
import type { ComponentType, ReactNode } from "react";
import type { LucideProps } from "lucide-react";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { cn } from "@/lib/utils";

export function SectionCard({
  title,
  icon: Icon,
  description,
  action,
  className,
  children,
}: {
  title: string;
  icon?: ComponentType<LucideProps>;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <Card className={cn("flex flex-col", className)}>
      <CardHeader className="flex flex-row items-start justify-between gap-3 space-y-0 p-5 pb-3">
        <div className="flex min-w-0 items-center gap-3">
          {Icon && (
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-muted">
              <Icon className="h-4 w-4 text-muted-foreground" strokeWidth={2} aria-hidden="true" />
            </span>
          )}
          <div className="min-w-0 space-y-1">
            <h2 className="font-display text-base font-semibold leading-none text-foreground">{title}</h2>
            {description && <p className="text-xs text-muted-foreground">{description}</p>}
          </div>
        </div>
        {action && <div className="shrink-0 text-[13px]">{action}</div>}
      </CardHeader>
      <CardContent className="flex-1 p-5 pt-0">{children}</CardContent>
    </Card>
  );
}
```

Create `src/components/page/KpiTile.tsx`:

```tsx
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export type KpiDeltaTone = "positive" | "negative" | "neutral";

// Tone colors are contrast-checked in tokens.contrast.test.ts (status-delivered, destructive on
// card). The delta text itself always carries the direction ("+0.4 pts"), so tone is a
// confirmation signal, never the only one.
const TONE_CLASS: Record<KpiDeltaTone, string> = {
  positive: "text-status-delivered",
  negative: "text-destructive",
  neutral: "text-muted-foreground",
};

export function KpiTile({
  label,
  value,
  delta,
  note,
}: {
  label: string;
  value: ReactNode;
  delta?: { text: string; tone: KpiDeltaTone };
  note?: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1.5 rounded-xl border border-border bg-card p-4">
      <span className="text-[12.5px] text-muted-foreground">{label}</span>
      <div className="flex flex-wrap items-baseline gap-2">
        <span className="text-2xl font-semibold tabular-nums tracking-tight text-foreground sm:text-[28px]">{value}</span>
        {delta && (
          <span data-tone={delta.tone} className={cn("text-xs font-medium", TONE_CLASS[delta.tone])}>
            {delta.text}
          </span>
        )}
      </div>
      {note && <span className="text-[11.5px] text-muted-foreground">{note}</span>}
    </div>
  );
}
```

Create `src/components/page/PipelineStrip.tsx`:

```tsx
import type { ReactNode } from "react";
import Link from "next/link";

export interface PipelineStage {
  key: string;
  label: string;
  count: number;
  /** A statusConfig token name, e.g. "status-in-transit". */
  token: string;
  /** 0–1; clamped. Omit to hide the bar. */
  share?: number;
  caption?: ReactNode;
  href?: string;
}

const TILE =
  "flex h-full flex-col gap-2 rounded-lg border border-border bg-background px-3.5 py-3 text-foreground transition-colors";

function StageBody({ stage }: { stage: PipelineStage }) {
  const share = stage.share === undefined ? undefined : Math.min(1, Math.max(0, stage.share));
  return (
    <>
      <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <span className="h-2 w-2 shrink-0 rounded-full" style={{ backgroundColor: `hsl(var(--${stage.token}))` }} aria-hidden="true" />
        {stage.label}
      </span>
      <span className="text-2xl font-semibold tabular-nums tracking-tight">{stage.count.toLocaleString("en-US")}</span>
      {share !== undefined && (
        <span className="block h-1 rounded-full bg-muted" aria-hidden="true">
          <span
            data-share-bar={stage.key}
            className="block h-1 rounded-full"
            style={{ width: `${Math.round(share * 100)}%`, backgroundColor: `hsl(var(--${stage.token}))` }}
          />
        </span>
      )}
      {stage.caption && <span className="text-[11.5px] text-muted-foreground">{stage.caption}</span>}
    </>
  );
}

export function PipelineStrip({ stages, ariaLabel }: { stages: PipelineStage[]; ariaLabel: string }) {
  return (
    <ol aria-label={ariaLabel} className="grid grid-cols-2 gap-2.5 sm:grid-cols-4 xl:auto-cols-fr xl:grid-flow-col xl:grid-cols-none">
      {stages.map((stage) => (
        <li key={stage.key}>
          {stage.href ? (
            <Link href={stage.href} className={`${TILE} hover:bg-accent`}>
              <StageBody stage={stage} />
            </Link>
          ) : (
            <div className={TILE}>
              <StageBody stage={stage} />
            </div>
          )}
        </li>
      ))}
    </ol>
  );
}
```

Create `src/components/page/SheetHeaderBlock.tsx`:

```tsx
import type { ReactNode } from "react";
import { SheetTitle } from "@/components/ui/sheet";
import { cn } from "@/lib/utils";

// Standard detail-Sheet header (adopted by Sheets in R2–R6). Title stays in the body face per
// DESIGN.md's Sheet-Stays-Plain Rule; `mono` is for AWB / manifest codes.
export function SheetHeaderBlock({
  eyebrow,
  title,
  mono = false,
  status,
  facts,
}: {
  eyebrow: string;
  title: ReactNode;
  mono?: boolean;
  status?: ReactNode;
  facts?: { label: string; value: ReactNode }[];
}) {
  return (
    <div className="space-y-4 border-b border-border pb-4">
      <div className="space-y-1.5">
        <p className="font-mono text-[10.5px] uppercase tracking-[0.12em] text-muted-foreground">{eyebrow}</p>
        <div className="flex flex-wrap items-center gap-2.5">
          <SheetTitle className={cn("text-xl", mono ? "font-mono font-medium" : "font-sans font-semibold")}>{title}</SheetTitle>
          {status}
        </div>
      </div>
      {facts && facts.length > 0 && (
        <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {facts.map((fact) => (
            <div key={fact.label} className="space-y-0.5">
              <dt className="text-[11.5px] text-muted-foreground">{fact.label}</dt>
              <dd className="text-[13px] font-medium text-foreground">{fact.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Point `StatTile` at `KpiTile`**

Replace `src/components/states/StatTile.tsx` with:

```tsx
import { KpiTile } from "@/components/page/KpiTile";

// Kept for existing callers; new code uses KpiTile directly.
export function StatTile({ label, value }: { label: string; value: string | number }) {
  return <KpiTile label={label} value={value} />;
}
```

- [ ] **Step 5: Adopt `PageHeader` in `DashboardHeader`**

Replace the returned JSX in `src/features/reports/components/DashboardHeader.tsx` with (keep `greeting` and the imports; add `import { PageHeader } from "@/components/page/PageHeader";`):

```tsx
  return (
    <PageHeader
      eyebrow={`${greeting(now.getHours())}${firstName ? `, ${firstName}` : ""}`}
      title={session?.organizationName ?? "Organization Dashboard"}
      description={`As of ${now.toLocaleDateString("en-US", { month: "short", day: "numeric" })}, ${now.toLocaleTimeString("en-US", { hour: "numeric", minute: "2-digit" })}`}
    />
  );
```

- [ ] **Step 6: Run tests**

Run: `npx vitest run src/components/page src/components/states src/features/reports "src/app/platform" "src/app/(staff)"`
Expected: PASS. `DashboardHeader.test.tsx` passes unchanged (same texts). If a page test asserted `StatTile`'s old `text-sm` label class, update only that class assertion.

- [ ] **Step 7: Commit**

```bash
git add src/components/page src/components/states/StatTile.tsx src/features/reports/components/DashboardHeader.tsx
git commit -m "feat(web): add page primitives and adopt PageHeader on dashboard (F15 R1)"
```

---

### Task 11: Restyle shared table, badge and states

**Files:**
- Modify: `src/components/data-table/DataTable.tsx`, `src/components/data-table/BorderedTableContainer.tsx`
- Modify: `src/components/status-badge/StatusBadge.tsx`
- Modify: `src/components/states/EmptyState.tsx`, `src/components/states/LoadingState.tsx`
- Test: `src/components/data-table/DataTable.test.tsx` (add cases)

**Interfaces:**
- Produces: `DataTableProps<T>` gains `selectedRowId?: string` (compared against `rowKey(row)`).

- [ ] **Step 1: Write the failing test**

Append to `src/components/data-table/DataTable.test.tsx` (reuse its existing imports; add any missing ones shown):

```tsx
import { render as renderSelected, screen as selectedScreen } from "@testing-library/react";

describe("DataTable selection", () => {
  const data = {
    items: [{ id: "a", name: "Alpha" }, { id: "b", name: "Beta" }],
    page: 1,
    pageSize: 20,
    totalCount: 2,
    totalPages: 1,
  };
  const columns = [{ key: "name", header: "Name", render: (r: { name: string }) => r.name }];

  it("marks the row matching selectedRowId as selected and no other", () => {
    renderSelected(
      <DataTable columns={columns} data={data} isLoading={false} emptyTitle="None" onPageChange={() => {}} rowKey={(r) => r.id} selectedRowId="b" />
    );
    expect(selectedScreen.getByText("Beta").closest("tr")).toHaveAttribute("aria-selected", "true");
    expect(selectedScreen.getByText("Alpha").closest("tr")).toHaveAttribute("aria-selected", "false");
  });

  it("omits aria-selected entirely when selection is not in use", () => {
    renderSelected(
      <DataTable columns={columns} data={data} isLoading={false} emptyTitle="None" onPageChange={() => {}} rowKey={(r) => r.id} />
    );
    expect(selectedScreen.getByText("Alpha").closest("tr")).not.toHaveAttribute("aria-selected");
  });
});
```

(If the file already imports `render`/`screen` and `DataTable`, drop the aliased import and use the existing names.)

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/components/data-table/DataTable.test.tsx`
Expected: FAIL — no `aria-selected`.

- [ ] **Step 3: Implement**

In `DataTable.tsx`:
- add `import { cn } from "@/lib/utils";` and `import { BorderedTableContainer } from "@/components/data-table/BorderedTableContainer";`
- add `selectedRowId?: string;` to `DataTableProps<T>` and destructure it
- replace the `<div className="overflow-hidden rounded-xl border border-border bg-muted/40">` … `</div>` wrapper with `<BorderedTableContainer>` … `</BorderedTableContainer>`
- replace the header/body rendering with:

```tsx
          <TableHeader className="bg-surface-sunken">
            <TableRow className="hover:bg-transparent">
              {columns.map((col) => (
                <TableHead key={col.key} className="h-10 text-[11.5px] font-medium text-muted-foreground">
                  {col.header}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.items.map((row) => {
              const id = rowKey(row);
              const selected = selectedRowId !== undefined && id === selectedRowId;
              return (
                <TableRow
                  key={id}
                  aria-selected={selectedRowId === undefined ? undefined : selected}
                  className={cn(selected && "bg-accent hover:bg-accent")}
                >
                  {columns.map((col) => (
                    <TableCell key={col.key} className="h-[52px] py-2">
                      {col.render(row)}
                    </TableCell>
                  ))}
                </TableRow>
              );
            })}
          </TableBody>
```

Replace `BorderedTableContainer.tsx`'s body and comment:

```tsx
import type { ReactNode } from "react";

export interface BorderedTableContainerProps {
  children: ReactNode;
}

// The one bordered table frame (DataTable and every raw Table). F15 R1: card surface on the sand
// page — cards and page no longer share a color, so bg-card now reads as a lifted panel.
export function BorderedTableContainer({ children }: BorderedTableContainerProps) {
  return <div className="overflow-hidden rounded-xl border border-border bg-card">{children}</div>;
}
```

In `StatusBadge.tsx` change the `Badge` className to `"h-6 gap-1.5 rounded-md px-2 text-xs font-medium"`.

In `EmptyState.tsx` change the wrapper classes to add `bg-card`:
`"flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed border-border bg-card p-12 text-center"`.

In `LoadingState.tsx`, change the container's `bg-muted/40` to `bg-card` (keep every other class).

- [ ] **Step 4: Run tests**

Run: `npx vitest run src/components`
Expected: PASS. Update only assertions on removed class strings (e.g. a test expecting `bg-muted/40`), never behavioural ones.

- [ ] **Step 5: Commit**

```bash
git add src/components/data-table src/components/status-badge/StatusBadge.tsx src/components/states/EmptyState.tsx src/components/states/LoadingState.tsx
git commit -m "feat(web): restyle tables, status badge and states for card-on-sand surfaces (F15 R1)"
```

---

### Task 12: Docs + full verification

**Files:**
- Modify: `DESIGN.md` (repo root), `docs/FRONTEND-ROADMAP.md`

- [ ] **Step 1: Update `DESIGN.md`**

Make these edits (keep the file's frontmatter/prose style):
1. Frontmatter `colors`: change `paper` from `#faf7f2` to `#fffdf9`, and add `sand: "#f4f1ea"`, `surface-sunken: "#f1eee7"`, `console-rail: "#161821"`, `console-rail-muted: "#8a8da0"`.
2. Colors → Neutral: describe **Sand** (`#f4f1ea`) as the page `background` and **Lokta Paper** (`#fffdf9`) as `card`/`popover`; add **Surface Sunken** (table header rows, Sheet footer bars).
3. Cards → Background: replace "paper (`bg-card`, identical to page `background` in light mode)" with "near-white card on the sand page — the two no longer share a value (F15 R1)". Data Table: replace the `bg-muted/40` rationale with the `bg-card` + `bg-surface-sunken` header description.
4. Navigation (Sidebar): replace the style bullets with the dark console rail (both themes), grouped non-collapsible section headings in Plex Mono caps, active item `bg-sidebar-accent` + white text + `aria-current="page"`, and "one `ConsoleShell` for staff, merchant and platform". Replace the Don't "build nested/collapsible sidebar nav groups speculatively" with "Don't make section headings collapsible or add sub-items — headings are labels, not groups."
5. Header: add breadcrumb (group › page), AD/BS dual date (`Asia/Kathmandu`), notifications bell (ComingSoonSlot until a feed exists).
6. Add a Named Rule under Components: **The No-Fake-Numbers Rule.** "Data the API does not serve yet renders `ComingSoonSlot` (`src/components/page/ComingSoonSlot.tsx`, `data-coming-soon`), never mock or sample values on a product screen."
7. Add a short "Page primitives" subsection listing `PageHeader`, `SectionCard`, `KpiTile`, `PipelineStrip`, `SheetHeaderBlock`, `ComingSoonSlot`.

- [ ] **Step 2: Update `docs/FRONTEND-ROADMAP.md`**

Insert before `## Later`:

```markdown
## Phase F15 — Console Redesign

Design source: claude.ai artifact "Courier Platform UI Redesign"
(https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu). Frontend only; data the API does not
serve renders `ComingSoonSlot`. Each sub-phase has its own spec → plan → implementation cycle.

| Sub-phase | Scope | Status |
|---|---|---|
| R1 — Console Foundation | Tokens, `ConsoleShell` (staff/merchant/platform), grouped dark rail, header (breadcrumb, AD/BS date, notifications slot), page primitives, table/badge restyle | Done — spec `docs/superpowers/specs/2026-09-30-f15-r1-console-foundation-design.md` |
| R2 — Staff operations | Dashboard, Shipments + detail Sheet, `/scan` Scan station | Planned |
| R3 — Staff finance & network | COD, manifests, NDR, RTO, riders, live map, branches | Planned |
| R4 — Rider PWA | Run screen, POD flow, scan, profile | Planned |
| R5 — Merchant portal + public tracking | 7 merchant pages, `/track` | Planned |
| R6 — Platform + admin | Platform pages, admin, audit, billing, auth pages | Planned |

**Backend follow-ups from R1:** notification feed (header bell); nav badge counts (e.g. open
NDRs); plan/branch usage in the session payload (sidebar plan card).
```

- [ ] **Step 3: Full verification**

Run each and confirm:
- `npm run lint` → no errors
- `npx tsc --noEmit` → no errors
- `npm test` → all pass; count ≥ Task 1 baseline minus the deleted `app-sidebar`/`nav-main`/`MerchantNavMain`/`PlatformNav` tests plus the new ones
- `npm run build` → succeeds
- `grep -rn "data-coming-soon" src --include=*.tsx | grep -v test` → only `ComingSoonSlot.tsx`

- [ ] **Step 4: Manual smoke (report results)**

`npm run dev`, then in light and dark theme, at desktop and 375 px width, open `/dashboard`, `/shipments`, `/riders/live-map`, `/merchant/dashboard`, `/platform/organizations`: rail shows grouped headings, the correct item is active, breadcrumb matches, mobile drawer opens and closes on navigation, no horizontal page scroll, the bell opens the "Needs backend" slot. Note anything that could not be checked (e.g. no local API).

- [ ] **Step 5: Commit**

```bash
git add DESIGN.md docs/FRONTEND-ROADMAP.md
git commit -m "docs: record F15 console redesign and R1 design-system changes"
```
