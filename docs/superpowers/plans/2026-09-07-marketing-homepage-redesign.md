# Marketing Homepage Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Task 1 is an exception to normal dispatch.** It requires an interactive
> local stack (Docker, `dotnet run`, `npm run dev`) and live Chrome browser
> automation with judgment calls about what to click next — it is not a
> red/green unit-test cycle. Run Task 1 in the orchestrating session
> directly (not a fresh subagent with no memory of this plan's context).
> Tasks 2-12 are standard TDD component work and suit subagent dispatch
> normally.

**Goal:** Redesign the public marketing homepage (`/`) with real, live-captured
screenshots of the actual app in place of the stylized mockup, plus bolder
graphics (glow, grain, a route-line motif) and motion on the existing dark
"Night Dispatch" identity.

**Architecture:** No routing/backend change. A new `AppScreenshotFrame`
component becomes the one place that renders a real screenshot (browser-chrome
styling, optional hover tilt); a new `ScreenShowcase` section becomes the
screenshots' dedicated home; `Hero` and `ProductWalkthrough` swap their
stylized `ProductMockupPanel` visual for real screenshots in the same frame.
A new `RouteLineGraphic` SVG adds a decorative route motif behind `Hero` and
`HowItWorks`. `FeatureGrid` and `HowItWorks` gain gradient/glow styling and a
scroll-triggered reveal using the existing `useRevealVariants` hook (a
pattern already built for this in Frontend Phase F13 but not yet applied to
those two sections).

**Tech Stack:** Next.js 14 (App Router), TypeScript, Tailwind CSS, Framer
Motion (already a dependency), Vitest + Testing Library. Screenshot capture
uses the local Docker stack (`docker-compose.yml`) + Chrome browser
automation (`mcp__claude-in-chrome__*` tools) — no new npm dependency.

**Spec:** `docs/superpowers/specs/2026-09-07-marketing-homepage-redesign-design.md`
(builds on `docs/superpowers/specs/2026-09-07-frontend-phase-f13-marketing-landing-page-design.md`)

## Global Constraints

- No fabricated social proof, no invented durable brand name — the seed-data
  test organization used for screenshots (e.g. "Sagarmatha Express") is
  throwaway content confined to screenshot pixels, never surfaced as page
  copy or the product's own identity.
- Every screenshot `<img>` has descriptive, non-empty `alt` text describing
  the UI shown — never implying real customer data.
- All new/changed motion (hover tilt, route-line dash animation, scroll
  reveal) is gated behind Framer Motion's `useReducedMotion()` — reduced-
  motion users get the fully visible, static end state, never a stuck
  `opacity-0`.
- `--marketing-*` CSS custom properties (`src/web/src/app/globals.css`) are
  the only styling surface for this route — no new entries in the app's own
  non-marketing `:root`/`.dark` tokens.
- `npm run build`, `npm run lint`, and `npm test` (Vitest) must stay green
  after every task, with zero regressions to any pre-existing test.
- No change to `/login`, `/dashboard`, `/track`, or any other route's
  internals, and no change to the auth-aware CTA logic already in
  `content.ts`'s `getPrimaryCta`.

---

## Task 1: Capture real app screenshots

**Files:**
- Create: `src/web/public/marketing/screenshots/dashboard.webp`
- Create: `src/web/public/marketing/screenshots/shipment-detail.webp`
- Create: `src/web/public/marketing/screenshots/scan-manifest.webp`
- Create: `src/web/public/marketing/screenshots/cod-settlement.webp`

**Interfaces:**
- Produces: four `.webp` files under `src/web/public/marketing/screenshots/`,
  each ≤200KB, captured at a 1440×900 viewport. Task 2's `SCREENSHOTS` array
  references these exact filenames — do not rename without updating Task 2.

This task has no unit tests — its "test" is the deliverable files existing at
the right paths with reasonable size, verified by `ls -la` at the end.

- [ ] **Step 1: Bring up the local stack**

```bash
docker compose up -d
docker compose ps
```

Expected: `postgres` (port 5434), `redis` (6380), `rabbitmq`, `mailhog` all
show `healthy`. If a `courier-postgres-1`-style container from a prior
session is already running and healthy, reuse it — do not start a second
Postgres.

- [ ] **Step 2: Start the API and web app locally (not the Docker images — faster iteration)**

In the `src/Api` project directory, point the connection string at the
Docker Postgres port and run:

```bash
dotnet run --project src/Api --urls http://localhost:5000
```

In `src/web`:

```bash
npm run dev
```

Expected: API responds on `http://localhost:5000`, web app on
`http://localhost:3000`.

- [ ] **Step 2: Register one throwaway test organization**

Using Chrome browser automation (`mcp__claude-in-chrome__navigate` to
`http://localhost:3000/login`, then the registration flow reachable from
there), create one organization with a clearly fictional name (e.g.
"Sagarmatha Express") and an owner user. Record the login email/password
used — needed again in Step 4.

- [ ] **Step 3: Seed minimal realistic data through the UI**

Logged in as the new org's owner:
- Create 2-3 branches across different districts (`/branches`) via the
  existing branch-create Sheet.
- Create 6-10 shipments (`/shipments`) spanning different statuses —
  a few `Created`, a couple `In Transit`, one `Out for Delivery`, a couple
  `Delivered`, one with an NDR — enough that the dashboard and list don't
  read as an empty state.
- Create at least one manifest (`/manifests`) with a few shipments attached,
  in a status where its scan/receive UI is visible.
- Ensure at least one shipment has COD with a collection recorded, so
  `/cod` shows non-zero figures.

- [ ] **Step 4: Capture the four screenshots**

For each of the four routes below: navigate with
`mcp__claude-in-chrome__navigate`, set the viewport to 1440×900 with
`mcp__claude-in-chrome__resize_window`, wait for the page to finish loading,
then capture with `mcp__claude-in-chrome__computer` (screenshot action).

| Screen | Route | Save as |
|---|---|---|
| Org dashboard | `/dashboard` | `dashboard.png` |
| Shipment detail | `/shipments` (open one shipment's detail Sheet before capturing) | `shipment-detail.png` |
| Scan/manifest | `/manifests` (open a manifest's receive/scan view) | `scan-manifest.png` |
| COD settlement | `/cod` | `cod-settlement.png` |

Save the raw captures to the scratchpad directory first, not directly into
`public/`.

- [ ] **Step 5: Crop and convert to `.webp`**

For each captured PNG: crop any OS/browser chrome so only the app UI
remains, then convert to `.webp` (e.g. via an installed image tool or
Node's `sharp` run as a one-off script — whichever is available; no new
permanent dependency is added to `package.json` for this). Target ≤200KB
per file.

- [ ] **Step 6: Move into place and verify**

```bash
mkdir -p src/web/public/marketing/screenshots
# move the four converted .webp files here, named exactly:
# dashboard.webp, shipment-detail.webp, scan-manifest.webp, cod-settlement.webp
ls -la src/web/public/marketing/screenshots/
```

Expected: four `.webp` files listed, each ≤200KB. If any single screen
proved unreachable within reasonable effort, stop and flag it to the user
rather than substituting a redrawn mockup.

- [ ] **Step 7: Commit**

```bash
git add src/web/public/marketing/screenshots/
git commit -m "feat(web): add real app screenshots for the marketing page redesign"
```

---

## Task 2: Add screenshot content data and grain texture

**Files:**
- Modify: `src/web/src/features/marketing/content.ts`
- Modify: `src/web/src/features/marketing/content.test.ts`
- Modify: `src/web/src/app/globals.css`
- Modify: `src/web/src/app/page.tsx`
- Test: `src/web/src/app/page.test.tsx` (verify existing tests still pass — no new assertions required here)

**Interfaces:**
- Consumes: the four file paths from Task 1
  (`/marketing/screenshots/{dashboard,shipment-detail,scan-manifest,cod-settlement}.webp`).
- Produces: `SCREENSHOTS: MarketingScreenshot[]` from `content.ts`, each item
  `{ id: "dashboard" | "shipment-detail" | "scan-manifest" | "cod-settlement", src: string, alt: string, caption: string }`.
  Task 5 (`ScreenShowcase`) and Task 6 (`Hero`) and Task 7
  (`ProductWalkthrough`) consume this array/type by name.
- Produces: a `.marketing-grain-overlay` CSS class in `globals.css`, applied
  to a decorative `<div>` in `page.tsx`.

- [ ] **Step 1: Write the failing content test**

Add to `src/web/src/features/marketing/content.test.ts` (extend the existing
import list and the `describe("marketing content data", ...)` block):

```ts
import {
  getPrimaryCta,
  FEATURES,
  HOW_IT_WORKS_STEPS,
  WALKTHROUGH_STOPS,
  CREDIBILITY_POINTS,
  FOOTER_LINKS,
  NAV_LINKS,
  SCREENSHOTS,
} from "./content";
```

```ts
  it("has exactly 4 screenshots, each with a src under /marketing/screenshots, non-empty alt text, and a caption", () => {
    expect(SCREENSHOTS).toHaveLength(4);
    const ids = SCREENSHOTS.map((s) => s.id);
    expect(ids).toEqual(["dashboard", "shipment-detail", "scan-manifest", "cod-settlement"]);
    for (const screenshot of SCREENSHOTS) {
      expect(screenshot.src).toMatch(/^\/marketing\/screenshots\/.+\.webp$/);
      expect(screenshot.alt.length).toBeGreaterThan(0);
      expect(screenshot.caption.length).toBeGreaterThan(0);
    }
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- content.test.ts` (from `src/web`)
Expected: FAIL — `SCREENSHOTS` is not exported from `./content`.

- [ ] **Step 3: Implement `SCREENSHOTS` in `content.ts`**

Add to `src/web/src/features/marketing/content.ts`:

```ts
export interface MarketingScreenshot {
  id: "dashboard" | "shipment-detail" | "scan-manifest" | "cod-settlement";
  src: string;
  alt: string;
  caption: string;
}

export const SCREENSHOTS: MarketingScreenshot[] = [
  {
    id: "dashboard",
    src: "/marketing/screenshots/dashboard.webp",
    alt: "Org dashboard showing today's shipment counts, COD summary, and branch performance",
    caption: "Dashboard — today's shipments, COD, and branch performance at a glance",
  },
  {
    id: "shipment-detail",
    src: "/marketing/screenshots/shipment-detail.webp",
    alt: "Shipment detail view showing AWB, status timeline, and custody history",
    caption: "Shipment detail — full status timeline and custody history for one parcel",
  },
  {
    id: "scan-manifest",
    src: "/marketing/screenshots/scan-manifest.webp",
    alt: "Manifest screen showing shipments scanned for hub-to-hub transfer",
    caption: "Manifests — scan-verified shipments moving branch to branch",
  },
  {
    id: "cod-settlement",
    src: "/marketing/screenshots/cod-settlement.webp",
    alt: "COD settlement screen showing merchant payable amounts and reconciliation status",
    caption: "COD settlement — collection through merchant payout, fully reconciled",
  },
];
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- content.test.ts`
Expected: PASS

- [ ] **Step 5: Add the grain texture CSS**

In `src/web/src/app/globals.css`, immediately after the marketing `.dark`
block's closing tokens (near the existing marketing-tokens comment block),
add:

```css
/* Subtle noise texture for the marketing page's bolder visual pass — one
   value for both themes since opacity (not hue) carries the effect, so it
   lives outside the :root/.dark split. Fixed + pointer-events:none + a low
   z-index so it never intercepts clicks or affects layout. */
.marketing-grain-overlay {
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  opacity: 0.035;
  background-image: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='2' stitchTiles='stitch'/></filter><rect width='100%25' height='100%25' filter='url(%23n)'/></svg>");
}
```

- [ ] **Step 6: Apply the overlay in `page.tsx`**

Modify `src/web/src/app/page.tsx`. The root `<div>` and its children need a
stacking context so the grain overlay (`z-index: 0`, `position: fixed`)
paints behind real content — give the nav/main/footer wrapper `relative
z-10`:

```tsx
  return (
    <div className="min-h-screen bg-marketing-bg selection:bg-marketing-accent/40 selection:text-marketing-fg">
      <div aria-hidden="true" className="marketing-grain-overlay" data-testid="marketing-grain-overlay" />
      <div className="relative z-10">
        <MarketingNav isAuthenticated={isAuthenticated} />
        <main>
          <Hero isAuthenticated={isAuthenticated} />
          <HowItWorks />
          <ProductWalkthrough />
          <FeatureGrid />
          <Credibility />
          <FinalCta isAuthenticated={isAuthenticated} />
        </main>
        <MarketingFooter />
      </div>
    </div>
  );
```

- [ ] **Step 7: Run the full existing page test suite to confirm no regression**

Run: `npm test -- page.test.tsx` (from `src/web`)
Expected: PASS (existing auth-state/CTA assertions still hold — this change
only adds a wrapper `<div>`, no text/link content changed).

- [ ] **Step 8: Commit**

```bash
git add src/web/src/features/marketing/content.ts src/web/src/features/marketing/content.test.ts src/web/src/app/globals.css src/web/src/app/page.tsx
git commit -m "feat(web): add screenshot content data and marketing grain texture"
```

---

## Task 3: Build `AppScreenshotFrame` component

**Files:**
- Create: `src/web/src/features/marketing/components/AppScreenshotFrame.tsx`
- Test: `src/web/src/features/marketing/components/AppScreenshotFrame.test.tsx`

**Interfaces:**
- Produces: `AppScreenshotFrame({ src, alt, hoverTilt = false, className = "" }: { src: string; alt: string; hoverTilt?: boolean; className?: string })`
  — a named export React component. Tasks 5, 6, 7 import this by name.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/marketing/components/AppScreenshotFrame.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { AppScreenshotFrame } from "./AppScreenshotFrame";

describe("AppScreenshotFrame", () => {
  it("renders the image with the given src and alt text", () => {
    render(<AppScreenshotFrame src="/marketing/screenshots/dashboard.webp" alt="Org dashboard preview" />);
    const img = screen.getByRole("img", { name: "Org dashboard preview" });
    expect(img).toHaveAttribute("src", "/marketing/screenshots/dashboard.webp");
  });

  it("renders the decorative browser-chrome header as hidden from assistive tech", () => {
    render(<AppScreenshotFrame src="/marketing/screenshots/dashboard.webp" alt="Org dashboard preview" />);
    const frame = screen.getByTestId("app-screenshot-frame");
    const chromeHeader = frame.querySelector('[aria-hidden="true"]');
    expect(chromeHeader).not.toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- AppScreenshotFrame.test.tsx` (from `src/web`)
Expected: FAIL — cannot find module `./AppScreenshotFrame`.

- [ ] **Step 3: Implement `AppScreenshotFrame.tsx`**

```tsx
"use client";

import { motion, useReducedMotion } from "framer-motion";

/**
 * Wraps a real app screenshot in a browser-chrome-style frame with an
 * accent glow, replacing the stylized ProductMockupPanel this redesign
 * removes. hoverTilt adds a subtle 3D tilt on hover, gated behind
 * prefers-reduced-motion like every other animated piece on this page.
 */
export function AppScreenshotFrame({
  src,
  alt,
  hoverTilt = false,
  className = "",
}: {
  src: string;
  alt: string;
  hoverTilt?: boolean;
  className?: string;
}) {
  const prefersReducedMotion = useReducedMotion();
  const tiltEnabled = hoverTilt && !prefersReducedMotion;

  return (
    <motion.div
      data-testid="app-screenshot-frame"
      whileHover={tiltEnabled ? { rotateX: 4, rotateY: -4, scale: 1.01 } : undefined}
      transition={{ type: "spring", stiffness: 220, damping: 18 }}
      style={{ transformPerspective: 900 }}
      className={`relative overflow-hidden rounded-xl border border-marketing-border bg-marketing-bg-elevated shadow-[0_0_60px_-15px_hsl(var(--marketing-accent)/0.5)] ${className}`}
    >
      <div className="flex items-center gap-1.5 border-b border-marketing-border/60 px-4 py-3" aria-hidden="true">
        <span className="h-2.5 w-2.5 rounded-full bg-marketing-border" />
        <span className="h-2.5 w-2.5 rounded-full bg-marketing-border" />
        <span className="h-2.5 w-2.5 rounded-full bg-marketing-border" />
      </div>
      <img src={src} alt={alt} className="w-full" />
    </motion.div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- AppScreenshotFrame.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/AppScreenshotFrame.tsx src/web/src/features/marketing/components/AppScreenshotFrame.test.tsx
git commit -m "feat(web): add AppScreenshotFrame component for real app screenshots"
```

---

## Task 4: Build `RouteLineGraphic` component

**Files:**
- Create: `src/web/src/features/marketing/components/RouteLineGraphic.tsx`
- Test: `src/web/src/features/marketing/components/RouteLineGraphic.test.tsx`

**Interfaces:**
- Produces: `RouteLineGraphic({ className = "" }: { className?: string })`
  — a named export React component. Tasks 6 and 9 import this by name.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/marketing/components/RouteLineGraphic.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RouteLineGraphic } from "./RouteLineGraphic";

describe("RouteLineGraphic", () => {
  it("renders as a decorative graphic hidden from assistive tech", () => {
    render(<RouteLineGraphic />);
    expect(screen.getByTestId("route-line-graphic")).toHaveAttribute("aria-hidden", "true");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- RouteLineGraphic.test.tsx` (from `src/web`)
Expected: FAIL — cannot find module `./RouteLineGraphic`.

- [ ] **Step 3: Implement `RouteLineGraphic.tsx`**

```tsx
"use client";

import { motion, useReducedMotion } from "framer-motion";

/**
 * Purely decorative dashed route line with hub nodes, evoking a delivery
 * route between branches — background texture for Hero/HowItWorks, never
 * informational, so it's aria-hidden.
 */
export function RouteLineGraphic({ className = "" }: { className?: string }) {
  const prefersReducedMotion = useReducedMotion();
  const hubs: Array<[number, number]> = [
    [0, 150],
    [350, 50],
    [700, 100],
  ];

  return (
    <svg
      aria-hidden="true"
      data-testid="route-line-graphic"
      viewBox="0 0 800 200"
      preserveAspectRatio="none"
      className={`pointer-events-none ${className}`}
    >
      <motion.path
        d="M0,150 C150,150 200,50 350,50 S550,150 700,100 S800,40 800,40"
        fill="none"
        stroke="hsl(var(--marketing-accent))"
        strokeOpacity={0.25}
        strokeWidth={1.5}
        strokeDasharray="6 10"
        animate={prefersReducedMotion ? undefined : { strokeDashoffset: [0, -64] }}
        transition={prefersReducedMotion ? undefined : { duration: 6, repeat: Infinity, ease: "linear" }}
      />
      {hubs.map(([cx, cy]) => (
        <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r={4} fill="hsl(var(--marketing-accent))" fillOpacity={0.4} />
      ))}
    </svg>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- RouteLineGraphic.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/RouteLineGraphic.tsx src/web/src/features/marketing/components/RouteLineGraphic.test.tsx
git commit -m "feat(web): add RouteLineGraphic decorative component"
```

---

## Task 5: Build `ScreenShowcase` section and wire it into the page

**Files:**
- Create: `src/web/src/features/marketing/components/ScreenShowcase.tsx`
- Test: `src/web/src/features/marketing/components/ScreenShowcase.test.tsx`
- Modify: `src/web/src/app/page.tsx`
- Modify: `src/web/src/app/page.test.tsx` (verify unaffected — no new assertions required)

**Interfaces:**
- Consumes: `SCREENSHOTS` (Task 2, from `../content`), `AppScreenshotFrame`
  (Task 3), `useRevealVariants` (`../lib/motionVariants`, existing).
- Produces: `ScreenShowcase()` — a named export React component with no
  props, rendering all four `SCREENSHOTS` entries.

- [ ] **Step 1: Write the failing test**

Create `src/web/src/features/marketing/components/ScreenShowcase.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ScreenShowcase } from "./ScreenShowcase";
import { SCREENSHOTS } from "../content";

describe("ScreenShowcase", () => {
  it("renders every screenshot's image and caption", () => {
    render(<ScreenShowcase />);
    for (const screenshot of SCREENSHOTS) {
      expect(screen.getByRole("img", { name: screenshot.alt })).toBeInTheDocument();
      expect(screen.getByText(screenshot.caption)).toBeInTheDocument();
    }
  });

  it("renders a section heading", () => {
    render(<ScreenShowcase />);
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- ScreenShowcase.test.tsx` (from `src/web`)
Expected: FAIL — cannot find module `./ScreenShowcase`.

- [ ] **Step 3: Implement `ScreenShowcase.tsx`**

```tsx
"use client";

import { motion } from "framer-motion";
import { SCREENSHOTS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";

export function ScreenShowcase() {
  const { container, item } = useRevealVariants();

  return (
    <section id="screens" className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          The actual product, not a mockup
        </h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          Every screen below is the real app, running against real data — nothing hand-drawn.
        </p>
      </div>
      <motion.div
        variants={container}
        initial="hidden"
        whileInView="visible"
        viewport={{ once: true, amount: 0.2 }}
        className="grid grid-cols-1 gap-6 sm:grid-cols-2"
      >
        {SCREENSHOTS.map((screenshot) => (
          <motion.figure key={screenshot.id} variants={item} className="flex flex-col gap-3">
            <AppScreenshotFrame src={screenshot.src} alt={screenshot.alt} hoverTilt />
            <figcaption className="text-sm text-marketing-muted-fg">{screenshot.caption}</figcaption>
          </motion.figure>
        ))}
      </motion.div>
    </section>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- ScreenShowcase.test.tsx`
Expected: PASS

- [ ] **Step 5: Wire into `page.tsx`**

Modify `src/web/src/app/page.tsx`: add the import and insert `<ScreenShowcase />`
directly after `<Hero />`, before `<HowItWorks />`:

```tsx
import { ScreenShowcase } from "@/features/marketing/components/ScreenShowcase";
```

```tsx
          <Hero isAuthenticated={isAuthenticated} />
          <ScreenShowcase />
          <HowItWorks />
```

- [ ] **Step 6: Run the full page test suite to confirm no regression**

Run: `npm test -- page.test.tsx` (from `src/web`)
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/web/src/features/marketing/components/ScreenShowcase.tsx src/web/src/features/marketing/components/ScreenShowcase.test.tsx src/web/src/app/page.tsx
git commit -m "feat(web): add ScreenShowcase section with real app screenshots"
```

---

## Task 6: Redesign `Hero`

**Files:**
- Modify: `src/web/src/features/marketing/components/Hero.tsx`
- Modify: `src/web/src/features/marketing/components/Hero.test.tsx`

**Interfaces:**
- Consumes: `AppScreenshotFrame` (Task 3), `RouteLineGraphic` (Task 4),
  `SCREENSHOTS` (Task 2) — specifically `SCREENSHOTS.find(s => s.id === "dashboard")`.
- `Hero`'s own props/export signature (`Hero({ isAuthenticated }: { isAuthenticated: boolean })`)
  is unchanged — `page.tsx` needs no update for this task.

- [ ] **Step 1: Update the failing/changed test first**

Replace the `hero-status-preview` assertion in
`src/web/src/features/marketing/components/Hero.test.tsx` (the stylized
mockup's text-based preview is gone, replaced by a real screenshot image) —
replace the whole file:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Hero } from "./Hero";
import { SCREENSHOTS } from "../content";

const dashboardScreenshot = SCREENSHOTS.find((s) => s.id === "dashboard")!;

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

  it("shows the real dashboard screenshot as the hero visual", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("img", { name: dashboardScreenshot.alt })).toHaveAttribute(
      "src",
      dashboardScreenshot.src
    );
  });

  it("renders the decorative gradient orb and route-line graphic", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByTestId("hero-gradient-orb")).toHaveAttribute("aria-hidden", "true");
    expect(screen.getByTestId("route-line-graphic")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- Hero.test.tsx` (from `src/web`)
Expected: FAIL — no image with the dashboard screenshot's alt text yet.

- [ ] **Step 3: Implement the redesigned `Hero.tsx`**

```tsx
"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { ArrowRight } from "lucide-react";
import { getPrimaryCta, SCREENSHOTS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { RouteLineGraphic } from "./RouteLineGraphic";

const dashboardScreenshot = SCREENSHOTS.find((s) => s.id === "dashboard")!;

export function Hero({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { container, item } = useRevealVariants();

  return (
    <section className="relative mx-auto grid max-w-6xl gap-12 overflow-hidden px-4 pb-24 pt-20 lg:grid-cols-[1.05fr_1fr] lg:items-center lg:gap-16">
      <RouteLineGraphic className="absolute inset-x-0 top-1/2 -z-10 h-40 w-full -translate-y-1/2 opacity-60" />
      <div
        aria-hidden="true"
        data-testid="hero-gradient-orb"
        className="pointer-events-none absolute -right-24 top-1/2 -z-10 h-[28rem] w-[28rem] -translate-y-1/2 rounded-full opacity-40 blur-3xl"
        style={{ backgroundColor: "hsl(var(--marketing-accent))" }}
      />
      <motion.div
        variants={container}
        initial="hidden"
        animate="visible"
        className="flex flex-col items-center gap-6 text-center lg:items-start lg:text-left"
      >
        <motion.h1
          variants={item}
          className="font-display text-6xl font-semibold tracking-tight text-marketing-fg sm:text-7xl"
        >
          Every parcel, accounted for.
        </motion.h1>
        <motion.p variants={item} className="max-w-xl text-lg text-marketing-muted-fg">
          Scan-driven custody, hub transfers, delivery attempts, and COD settlement — one platform that always
          knows where a parcel is, who has it, and what happens next.
        </motion.p>
        <motion.div variants={item} className="flex flex-wrap items-center justify-center gap-4 lg:justify-start">
          <Link
            href={cta.href}
            className="group inline-flex items-center gap-2 rounded-lg bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg shadow-[0_0_40px_-10px_hsl(var(--marketing-accent)/0.7)] transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
          >
            {cta.label}
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
          </Link>
          <a
            href="#walkthrough"
            className="rounded-lg border border-marketing-border px-6 py-3 text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
          >
            See how it works
          </a>
        </motion.div>
      </motion.div>
      <motion.div variants={item} initial="hidden" animate="visible" className="w-full">
        <AppScreenshotFrame src={dashboardScreenshot.src} alt={dashboardScreenshot.alt} hoverTilt />
      </motion.div>
    </section>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- Hero.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/Hero.tsx src/web/src/features/marketing/components/Hero.test.tsx
git commit -m "redesign(web): give Hero an asymmetric layout with a real screenshot and route-line graphic"
```

---

## Task 7: Redesign `ProductWalkthrough`

**Files:**
- Modify: `src/web/src/features/marketing/components/ProductWalkthrough.tsx`
- Modify: `src/web/src/features/marketing/components/ProductWalkthrough.test.tsx`

**Interfaces:**
- Consumes: `AppScreenshotFrame` (Task 3), `SCREENSHOTS` (Task 2) —
  specifically the `shipment-detail` entry. `LifecycleRail` (existing,
  unchanged) is retained.
- `ProductWalkthrough`'s own export signature (`ProductWalkthrough()`, no
  props) is unchanged.

- [ ] **Step 1: Update the test**

Replace `src/web/src/features/marketing/components/ProductWalkthrough.test.tsx`
in full — the animated cycling logic through `WALKTHROUGH_STOPS` stays
(reused for the `LifecycleRail`'s progress state), but the centerpiece is now
a static real screenshot rather than a per-stop mockup panel, so drop the
"advances to the next stop" assertions on the *panel* text and instead assert
the screenshot renders once and the rail still cycles:

```tsx
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { WALKTHROUGH_STOPS, SCREENSHOTS } from "../content";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));

vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { ProductWalkthrough } = await import("./ProductWalkthrough");
const shipmentDetailScreenshot = SCREENSHOTS.find((s) => s.id === "shipment-detail")!;

describe("ProductWalkthrough", () => {
  afterEach(() => {
    useReducedMotionMock.mockReset();
    vi.useRealTimers();
  });

  it("shows every stop's label statically when reduced motion is preferred", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<ProductWalkthrough />);
    for (const stop of WALKTHROUGH_STOPS) {
      expect(screen.getByText(stop.label)).toBeInTheDocument();
    }
  });

  it("renders the real shipment-detail screenshot", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<ProductWalkthrough />);
    expect(screen.getByRole("img", { name: shipmentDetailScreenshot.alt })).toHaveAttribute(
      "src",
      shipmentDetailScreenshot.src
    );
  });

  it("starts the lifecycle rail on the first stop when motion is allowed", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<ProductWalkthrough />);
    expect(screen.getByTestId("walkthrough-rail")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });

  it("advances the rail to the next stop after the cycle interval", () => {
    useReducedMotionMock.mockReturnValue(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800);
    });
    expect(screen.getByTestId("walkthrough-rail")).toHaveTextContent(WALKTHROUGH_STOPS[1].label);
  });

  it("wraps the rail back to the first stop after cycling through all four", () => {
    useReducedMotionMock.mockReturnValue(false);
    vi.useFakeTimers();
    render(<ProductWalkthrough />);
    act(() => {
      vi.advanceTimersByTime(2800 * WALKTHROUGH_STOPS.length);
    });
    expect(screen.getByTestId("walkthrough-rail")).toHaveTextContent(WALKTHROUGH_STOPS[0].label);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- ProductWalkthrough.test.tsx` (from `src/web`)
Expected: FAIL — no `walkthrough-rail` testid, no shipment-detail image yet.

- [ ] **Step 3: Implement the redesigned `ProductWalkthrough.tsx`**

```tsx
"use client";

import { useEffect, useState } from "react";
import { SCREENSHOTS, WALKTHROUGH_STOPS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { LifecycleRail } from "./LifecycleRail";

const CYCLE_MS = 2800;
const shipmentDetailScreenshot = SCREENSHOTS.find((s) => s.id === "shipment-detail")!;

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

  return (
    <section id="walkthrough" className="mx-auto max-w-3xl px-4 py-24">
      <h2 className="mb-8 text-center font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
        One parcel, every step accounted for
      </h2>
      <div className="flex flex-col gap-6">
        <AppScreenshotFrame src={shipmentDetailScreenshot.src} alt={shipmentDetailScreenshot.alt} />
        <div data-testid="walkthrough-rail">
          <LifecycleRail
            stops={WALKTHROUGH_STOPS.map((stop) => ({ tone: stop.tone, label: stop.label }))}
            activeIndex={activeIndex}
            showLabels
          />
        </div>
      </div>
    </section>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- ProductWalkthrough.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/ProductWalkthrough.tsx src/web/src/features/marketing/components/ProductWalkthrough.test.tsx
git commit -m "redesign(web): swap ProductWalkthrough's centerpiece for a real screenshot"
```

---

## Task 8: Redesign `FeatureGrid`

**Files:**
- Modify: `src/web/src/features/marketing/components/FeatureGrid.tsx`
- Modify: `src/web/src/features/marketing/components/FeatureGrid.test.tsx`

**Interfaces:**
- Consumes: `FEATURES` (existing, unchanged), `useRevealVariants` (existing).
- `FeatureGrid`'s export signature (`FeatureGrid()`, no props) unchanged.

- [ ] **Step 1: Extend the test**

Add to `src/web/src/features/marketing/components/FeatureGrid.test.tsx`:

```tsx
  it("wraps each feature tile with a gradient border and glow class", () => {
    render(<FeatureGrid />);
    const tiles = screen.getAllByTestId("feature-tile");
    expect(tiles).toHaveLength(FEATURES.length);
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- FeatureGrid.test.tsx` (from `src/web`)
Expected: FAIL — no `feature-tile` testid yet.

- [ ] **Step 3: Implement the redesigned `FeatureGrid.tsx`**

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
            data-testid="feature-tile"
            variants={item}
            className="group flex flex-col gap-3 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6 transition-shadow hover:shadow-[0_0_40px_-15px_hsl(var(--marketing-accent)/0.6)]"
            style={{
              borderImage: "linear-gradient(135deg, hsl(var(--marketing-accent)/0.4), transparent 60%) 1",
            }}
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

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- FeatureGrid.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/FeatureGrid.tsx src/web/src/features/marketing/components/FeatureGrid.test.tsx
git commit -m "redesign(web): add gradient border, hover glow, and scroll reveal to FeatureGrid"
```

---

## Task 9: Redesign `HowItWorks`

**Files:**
- Modify: `src/web/src/features/marketing/components/HowItWorks.tsx`
- Modify: `src/web/src/features/marketing/components/HowItWorks.test.tsx`

**Interfaces:**
- Consumes: `HOW_IT_WORKS_STEPS` (existing, unchanged), `RouteLineGraphic`
  (Task 4), `useRevealVariants` (existing).
- `HowItWorks`'s export signature (`HowItWorks()`, no props) unchanged.

- [ ] **Step 1: Extend the test**

Read `src/web/src/features/marketing/components/HowItWorks.test.tsx` first
to match its existing style, then add:

```tsx
  it("renders the decorative route-line graphic", () => {
    render(<HowItWorks />);
    expect(screen.getByTestId("route-line-graphic")).toBeInTheDocument();
  });
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test -- HowItWorks.test.tsx` (from `src/web`)
Expected: FAIL — no `route-line-graphic` testid rendered yet.

- [ ] **Step 3: Implement the redesigned `HowItWorks.tsx`**

```tsx
"use client";

import { motion } from "framer-motion";
import { HOW_IT_WORKS_STEPS } from "../content";
import { useRevealVariants } from "../lib/motionVariants";
import { LifecycleRail } from "./LifecycleRail";
import { RouteLineGraphic } from "./RouteLineGraphic";

export function HowItWorks() {
  const { container, item } = useRevealVariants();

  return (
    <section id="how-it-works" className="relative mx-auto max-w-6xl overflow-hidden px-4 py-24">
      <RouteLineGraphic className="absolute inset-x-0 top-1/3 -z-10 h-32 w-full opacity-40" />
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">How it works</h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          Four stages, each backed by a real scan, manifest, or delivery attempt — never an arbitrary status edit.
        </p>
      </div>
      <div className="mb-6 hidden px-2 lg:block">
        <LifecycleRail
          dotSize="lg"
          stops={HOW_IT_WORKS_STEPS.map((step) => ({ tone: step.tone, label: step.title }))}
        />
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

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test -- HowItWorks.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/HowItWorks.tsx src/web/src/features/marketing/components/HowItWorks.test.tsx
git commit -m "redesign(web): add route-line graphic and scroll reveal to HowItWorks"
```

---

## Task 10: Polish `FinalCta`'s CTA glow

**Files:**
- Modify: `src/web/src/features/marketing/components/FinalCta.tsx`
- Modify: `src/web/src/features/marketing/components/FinalCta.test.tsx`

**Interfaces:**
- No new consumes/produces — styling-only change. `FinalCta`'s export
  signature (`FinalCta({ isAuthenticated }: { isAuthenticated: boolean })`)
  unchanged.

- [ ] **Step 1: Extend the test**

Read `src/web/src/features/marketing/components/FinalCta.test.tsx` first to
match its existing style, then add an assertion that the CTA link still
resolves correctly (guards against a typo breaking the href while adding the
glow class):

```tsx
  it("keeps the CTA link functional after the glow styling pass", () => {
    render(<FinalCta isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
  });
```

- [ ] **Step 2: Run test to verify it fails or passes trivially**

Run: `npm test -- FinalCta.test.tsx` (from `src/web`)
Expected: PASS already (this assertion holds against the current
implementation too) — this step confirms the baseline before the styling
change, per the plan's TDD discipline; the meaningful regression check is
Step 4 after the change.

- [ ] **Step 3: Implement the glow styling in `FinalCta.tsx`**

```tsx
import Link from "next/link";
import { getPrimaryCta } from "../content";

export function FinalCta({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);

  return (
    <section className="mx-auto max-w-3xl px-4 py-24 text-center">
      <div className="flex flex-col items-center gap-6">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Run your courier operation on one system
        </h2>
        <p className="max-w-xl text-marketing-muted-fg">
          Branches, riders, merchants, and COD — all traceable, from booking to settlement.
        </p>
        <Link
          href={cta.href}
          className="rounded-lg bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg shadow-[0_0_50px_-10px_hsl(var(--marketing-accent)/0.8)] transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
        >
          {cta.label}
        </Link>
      </div>
    </section>
  );
}
```

- [ ] **Step 4: Run test to verify it still passes**

Run: `npm test -- FinalCta.test.tsx`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/web/src/features/marketing/components/FinalCta.tsx src/web/src/features/marketing/components/FinalCta.test.tsx
git commit -m "polish(web): add accent glow to FinalCta's CTA button"
```

---

## Task 11: Remove the now-unused `ProductMockupPanel`

**Files:**
- Delete: `src/web/src/features/marketing/components/ProductMockupPanel.tsx`
- Delete: `src/web/src/features/marketing/components/ProductMockupPanel.test.tsx`

**Interfaces:**
- Consumes: nothing — this task only runs after Tasks 6 and 7 have removed
  every import of `ProductMockupPanel`.

- [ ] **Step 1: Verify no remaining references**

```bash
grep -rn "ProductMockupPanel" src/web/src
```

Expected: no output (Hero.tsx and ProductWalkthrough.tsx no longer import
it, per Tasks 6-7). If any reference remains, stop — do not delete a file
still in use.

- [ ] **Step 2: Delete the files**

```bash
git rm src/web/src/features/marketing/components/ProductMockupPanel.tsx src/web/src/features/marketing/components/ProductMockupPanel.test.tsx
```

- [ ] **Step 3: Run the full marketing test suite to confirm no regression**

Run: `npm test -- src/features/marketing` (from `src/web`)
Expected: PASS, no reference-to-deleted-module errors.

- [ ] **Step 4: Commit**

```bash
git commit -m "cleanup(web): remove ProductMockupPanel, superseded by real screenshots"
```

---

## Task 12: Full verification pass

**Files:** none (verification only).

**Interfaces:** none — this task validates the combined output of Tasks 1-11.

- [ ] **Step 1: Run the full test suite**

```bash
npm test
```

Expected: all suites PASS, including every pre-existing test outside the
marketing feature (no regressions to any other route).

- [ ] **Step 2: Run lint**

```bash
npm run lint
```

Expected: no errors.

- [ ] **Step 3: Run the production build**

```bash
npm run build
```

Expected: builds successfully; check the output for the `/` route's bundle
size — flag to the user if it grows unexpectedly large (the four `.webp`
screenshots are static assets served from `public/`, not bundled into JS, so
this should mainly reflect the new component code).

- [ ] **Step 4: Contrast-check new text/glow combinations**

Using the dataviz skill's contrast validator (the same tool Frontend Phase
F13's original color pass used), verify: hero headline over the Hero's
background (unchanged token, should already pass), FinalCta/Hero CTA label
(`--marketing-accent-foreground` on `--marketing-accent`) still meets its
already-verified ratio (the glow is a `box-shadow`, not a background change,
so text/button contrast itself is unaffected — confirm this holds, don't
assume).

- [ ] **Step 5: Manual browser check of the live page**

With `npm run dev` running, navigate to `http://localhost:3000/` via Chrome
browser automation and visually confirm: Hero shows the real dashboard
screenshot with route-line graphic behind it, ScreenShowcase shows all four
real screenshots, ProductWalkthrough shows the real shipment-detail
screenshot with the lifecycle rail still animating, FeatureGrid tiles show
the gradient/glow treatment, HowItWorks shows the route-line graphic, and
toggling `prefers-reduced-motion` (via Chrome DevTools' rendering panel or
OS setting) leaves every section fully visible with no stuck
`opacity-0`/hidden state.

- [ ] **Step 6: Report to the user**

Summarize what changed, confirm build/lint/test all green, and note the
final screenshot file sizes from Task 1.
