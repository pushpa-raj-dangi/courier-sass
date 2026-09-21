# Marketing Visual Redesign ("Custody Thread") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the public marketing surface (`/`, `/pricing`, `/track`, auth showcase panel) around a scroll-linked "custody thread" with editorial hero, CSS-built product cards, and three inline-SVG diagrams, keeping the Night Dispatch tokens.

**Architecture:** A `ThreadLayout` client wrapper draws one vertical accent rail down the page (scaleY driven by `useScroll`); each `SectionShell` places a numbered `Checkpoint` on that rail. Diagrams are server-renderable inline SVG generated from data + pure math helpers. Motion is confined to small client leaves (`ThreadLayout`, `Checkpoint`, `Reveal`, `CountUp`) plus one CSS keyframe; everything falls back to a fully-visible static state under `prefers-reduced-motion`.

**Tech Stack:** Next.js 14 (app router), React 18, Tailwind 3, Framer Motion 13, lucide-react, Vitest + Testing Library (jsdom). No new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-21-marketing-visual-redesign-design.md`

## Global Constraints

- All work is under `src/web`; run every command from `D:\courier\src\web`.
- No new libraries (Framer Motion, Tailwind, lucide only).
- `--marketing-*` tokens never appear in staff/merchant/platform/rider surfaces; auth pages keep fixed `--auth-panel-*` tokens (Two-Room Rule).
- Existing `--marketing-*` token values are unchanged. No purple/pink gradients, no glassmorphism (no `backdrop-blur` on new surfaces), radii 4–8px (`rounded-sm`/`rounded-md`/`rounded-lg` — never `rounded-xl`+ on marketing cards), hairline `--marketing-border` borders.
- No fabricated brand name, testimonials, customer counts, or metrics. All mock data is fictional and visibly labelled "Sample data".
- Brand text stays "Courier Platform". CTAs keep using `getPrimaryCta(isAuthenticated)` (`/register` or `/dashboard`).
- Exactly one `h1` per page; heading levels never skip.
- Every animation is gated on `useReducedMotion` (JS) or `@media (prefers-reduced-motion: reduce)` (CSS) and the static state is fully visible.
- Animate only `transform` / `opacity` / `stroke-dashoffset`.
- WCAG AA in light and dark: only use existing token-derived colors (`marketing-fg`, `-muted-fg`, `-accent`, `-accent-fg`, `-border`, `-bg`, `-bg-elevated`, `--marketing-tone-*`).
- Type: Space Grotesk (`font-display`) for headings, IBM Plex Mono (`font-mono`, already loaded) only for AWBs, numerals, stamps, labels.
- Commit trailer on every commit: `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Definition of done per task: that task's tests pass; final task runs `npm test`, `npm run lint`, `npm run build`.

## File Structure

New under `src/web/src/features/marketing/`:

| File | Responsibility |
|---|---|
| `lib/diagramMath.ts` (+test) | Pure functions: `codBreakdown`, `reconcileManifest`, `formatNpr` |
| `components/MonoLabel.tsx` | Mono uppercase label/stamp |
| `components/MockCard.tsx` | Fictional-data UI card + `ScanBar` |
| `components/CountUp.tsx` | One-time number count-up |
| `components/Reveal.tsx` | Scroll-reveal wrapper |
| `components/Checkpoint.tsx` | Numbered node on the thread |
| `components/ThreadLayout.tsx` | Scroll-linked rail wrapper |
| `components/SectionShell.tsx` | Section frame: checkpoint, eyebrow, heading, lede |
| `components/MarketingGrid.tsx` | Decorative faint grid background (Hero, /track) |
| `components/StageList.tsx` | Vertical stage rail list (COD / NDR / RTO) |
| `components/CustodyChainDiagram.tsx` | SVG custody chain |
| `components/CodWaterfallDiagram.tsx` | SVG COD waterfall |
| `components/ManifestReconcileDiagram.tsx` | SVG manifest reconcile grid |
| `components/PromiseStrip.tsx` | Three-claim strip between Hero and thread |
| `components/CustodySection.tsx`, `ScanSection.tsx`, `CodSection.tsx`, `ExceptionsSection.tsx`, `RolesSection.tsx` | Page sections 01–05 |

Modified: `content.ts`, `Hero.tsx`, `ScreenShowcase.tsx` (section 06), `FinalCta.tsx`, `MarketingNav.tsx`, `MarketingFooter.tsx`, `PricingPlans.tsx`, `PricingFaq.tsx`, `src/app/page.tsx`, `src/app/pricing/page.tsx` (if needed), `src/app/track/page.tsx`, `src/app/track/[awb]/page.tsx`, `src/features/tracking/components/TrackingTimeline.tsx`, `src/components/auth/AuthShowcasePanel.tsx`, `src/app/globals.css`, `DESIGN.md`, `PRODUCT.md`, `docs/FRONTEND-ROADMAP.md`, the spec.

Removed (after superseded): `HowItWorks`, `FeatureGrid`, `ProductWalkthrough`, `Credibility` (+ tests). `RouteLineGraphic` and `LifecycleRail` are removed only if grep shows no remaining importers.

Tests live beside components as `*.test.tsx`. Run a single test with `npx vitest run <path>`.

---

### Task 1: Math helpers, primitives, and CSS keyframes

**Files:**
- Create: `src/web/src/features/marketing/lib/diagramMath.ts`, `lib/diagramMath.test.ts`
- Create: `components/MonoLabel.tsx`, `MockCard.tsx`, `CountUp.tsx`, `Reveal.tsx`, `Checkpoint.tsx`, `ThreadLayout.tsx`, `SectionShell.tsx`, `MarketingGrid.tsx` and a `*.test.tsx` for each of MockCard, CountUp, Checkpoint, ThreadLayout, SectionShell
- Modify: `src/web/src/app/globals.css` (after `.marketing-grain-overlay` rule)

**Interfaces:**
- Produces:
  - `codBreakdown(collected: number, fee: number): { collected: number; fee: number; payable: number }`
  - `reconcileManifest(expected: number, missingIndices: number[]): { expected: number; scanned: number; missing: number; missingIndices: number[] }`
  - `formatNpr(n: number): string` → `"NPR 2,500"`
  - `<MonoLabel className?>`; `<MockCard label: string className?>`; `<ScanBar />`; `<CountUp value: number className? />`; `<Reveal className?>`; `<Checkpoint index: number />`; `<ThreadLayout>`; `<SectionShell id: string index: number eyebrow: string title: ReactNode lede?: ReactNode tone?: "base" | "elevated">`; `<MarketingGrid className? />`
  - CSS class `.marketing-scan-line`

- [ ] **Step 1: Write failing math tests**

`lib/diagramMath.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { codBreakdown, formatNpr, reconcileManifest } from "./diagramMath";

describe("codBreakdown", () => {
  it("computes merchant payable as collected minus courier fee (CLAUDE.md §22 example)", () => {
    expect(codBreakdown(2500, 150)).toEqual({ collected: 2500, fee: 150, payable: 2350 });
  });
  it("allows a zero fee", () => {
    expect(codBreakdown(1000, 0).payable).toBe(1000);
  });
  it("rejects a fee larger than the collected amount", () => {
    expect(() => codBreakdown(100, 150)).toThrow(RangeError);
  });
  it("rejects negative amounts", () => {
    expect(() => codBreakdown(-1, 0)).toThrow(RangeError);
    expect(() => codBreakdown(100, -1)).toThrow(RangeError);
  });
});

describe("reconcileManifest", () => {
  it("counts scanned = expected - missing", () => {
    expect(reconcileManifest(120, [87])).toEqual({ expected: 120, scanned: 119, missing: 1, missingIndices: [87] });
  });
  it("de-duplicates, sorts, and drops out-of-range indices", () => {
    expect(reconcileManifest(10, [5, 2, 5, 99, -1]).missingIndices).toEqual([2, 5]);
  });
  it("reports zero missing for a clean manifest", () => {
    expect(reconcileManifest(10, [])).toMatchObject({ scanned: 10, missing: 0 });
  });
});

describe("formatNpr", () => {
  it("prefixes NPR and groups thousands", () => {
    expect(formatNpr(2500)).toBe("NPR 2,500");
    expect(formatNpr(150)).toBe("NPR 150");
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/features/marketing/lib/diagramMath.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement `lib/diagramMath.ts`**

```ts
export interface CodBreakdown {
  collected: number;
  fee: number;
  payable: number;
}

/** Merchant payable = COD collected − courier fee (CLAUDE.md §22). Traceable, never silently adjusted. */
export function codBreakdown(collected: number, fee: number): CodBreakdown {
  if (collected < 0 || fee < 0) throw new RangeError("Amounts must be non-negative");
  if (fee > collected) throw new RangeError("Courier fee cannot exceed collected COD");
  return { collected, fee, payable: collected - fee };
}

export interface ManifestReconciliation {
  expected: number;
  scanned: number;
  missing: number;
  missingIndices: number[];
}

export function reconcileManifest(expected: number, missingIndices: number[]): ManifestReconciliation {
  const unique = Array.from(new Set(missingIndices))
    .filter((index) => index >= 0 && index < expected)
    .sort((a, b) => a - b);
  return { expected, scanned: expected - unique.length, missing: unique.length, missingIndices: unique };
}

export function formatNpr(amount: number): string {
  return `NPR ${amount.toLocaleString("en-US")}`;
}
```

- [ ] **Step 4: Run to verify pass**

Run: `npx vitest run src/features/marketing/lib/diagramMath.test.ts` — Expected: PASS.

- [ ] **Step 5: Add CSS keyframes** — in `globals.css`, directly after the `.marketing-grain-overlay { … }` rule:

```css
/* Scan-line sweep used by MockCard's ScanBar. Transform-only; static (full bar) under reduced motion. */
@keyframes marketing-scan-sweep {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(400%);
  }
}
.marketing-scan-line {
  animation: marketing-scan-sweep 2.8s ease-in-out infinite;
}
@media (prefers-reduced-motion: reduce) {
  .marketing-scan-line {
    animation: none;
    transform: none;
    width: 100%;
  }
}
```

- [ ] **Step 6: Write failing primitive tests**

`components/MockCard.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MockCard, ScanBar } from "./MockCard";

describe("MockCard", () => {
  it("renders its label, children, and the Sample data disclosure", () => {
    render(<MockCard label="Scan received">body text</MockCard>);
    expect(screen.getByText("Scan received")).toBeInTheDocument();
    expect(screen.getByText("body text")).toBeInTheDocument();
    expect(screen.getByText("Sample data")).toBeInTheDocument();
  });
});

describe("ScanBar", () => {
  it("is decorative and carries the scan-line class", () => {
    const { container } = render(<ScanBar />);
    expect(container.firstElementChild).toHaveAttribute("aria-hidden", "true");
    expect(container.querySelector(".marketing-scan-line")).not.toBeNull();
  });
});
```

`components/CountUp.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));
vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { CountUp } = await import("./CountUp");

describe("CountUp", () => {
  it("renders the final formatted value in the initial markup (no-JS / SSR safe)", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<CountUp value={2500} />);
    expect(screen.getByText("2,500")).toBeInTheDocument();
  });
  it("renders the final value immediately under reduced motion", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<CountUp value={2350} />);
    expect(screen.getByText("2,350")).toBeInTheDocument();
  });
});
```

`components/Checkpoint.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));
vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { Checkpoint } = await import("./Checkpoint");

describe("Checkpoint", () => {
  it("shows a zero-padded numeral and is decorative", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(<Checkpoint index={3} />);
    const node = screen.getByTestId("checkpoint");
    expect(node).toHaveTextContent("03");
    expect(node).toHaveAttribute("aria-hidden", "true");
  });
  it("is active (lit) by default under reduced motion", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<Checkpoint index={1} />);
    expect(screen.getByTestId("checkpoint")).toHaveAttribute("data-active", "true");
  });
});
```

`components/ThreadLayout.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const { useReducedMotionMock } = vi.hoisted(() => ({ useReducedMotionMock: vi.fn() }));
vi.mock("framer-motion", async () => {
  const actual = await vi.importActual<typeof import("framer-motion")>("framer-motion");
  return { ...actual, useReducedMotion: useReducedMotionMock };
});

const { ThreadLayout } = await import("./ThreadLayout");

describe("ThreadLayout", () => {
  it("renders children and a decorative thread", () => {
    useReducedMotionMock.mockReturnValue(false);
    render(
      <ThreadLayout>
        <p>content</p>
      </ThreadLayout>
    );
    expect(screen.getByText("content")).toBeInTheDocument();
    expect(screen.getByTestId("thread")).toHaveAttribute("aria-hidden", "true");
  });
  it("draws the full progress rail statically under reduced motion", () => {
    useReducedMotionMock.mockReturnValue(true);
    render(<ThreadLayout>x</ThreadLayout>);
    expect(screen.getByTestId("thread-progress").style.transform).toContain("scaleY(1)");
  });
});
```

`components/SectionShell.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { SectionShell } from "./SectionShell";

describe("SectionShell", () => {
  it("renders an h2 wired to the section via aria-labelledby, with a numbered eyebrow and lede", () => {
    const { container } = render(
      <SectionShell id="demo" index={2} eyebrow="Scan" title="Demo title" lede="Demo lede">
        <p>child</p>
      </SectionShell>
    );
    const heading = screen.getByRole("heading", { level: 2, name: "Demo title" });
    expect(heading).toHaveAttribute("id", "demo-heading");
    expect(container.querySelector("section#demo")).toHaveAttribute("aria-labelledby", "demo-heading");
    expect(screen.getByText("02 — Scan")).toBeInTheDocument();
    expect(screen.getByText("Demo lede")).toBeInTheDocument();
    expect(screen.getByText("child")).toBeInTheDocument();
  });
});
```

- [ ] **Step 7: Run to verify failure**

Run: `npx vitest run src/features/marketing/components/MockCard.test.tsx src/features/marketing/components/CountUp.test.tsx src/features/marketing/components/Checkpoint.test.tsx src/features/marketing/components/ThreadLayout.test.tsx src/features/marketing/components/SectionShell.test.tsx`
Expected: FAIL (modules not found).

- [ ] **Step 8: Implement primitives**

`components/MonoLabel.tsx`:

```tsx
import type { ReactNode } from "react";

/** Small uppercase Plex Mono label/stamp — reserved for AWBs, numerals, and eyebrows. */
export function MonoLabel({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <span
      className={`font-mono text-[0.6875rem] font-medium uppercase tracking-[0.14em] text-marketing-muted-fg ${className}`}
    >
      {children}
    </span>
  );
}
```

`components/MockCard.tsx`:

```tsx
import type { ReactNode } from "react";
import { MonoLabel } from "./MonoLabel";

/** Thin bar with a sweeping scan line; the sweep is CSS-only and static under prefers-reduced-motion. */
export function ScanBar() {
  return (
    <div aria-hidden="true" className="h-0.5 w-full overflow-hidden rounded-sm bg-marketing-border/40">
      <div className="marketing-scan-line h-full w-1/4 bg-marketing-accent" />
    </div>
  );
}

/**
 * CSS-built UI card showing fictional sample data. The hard offset shadow (not blur) gives depth
 * in the editorial style; the "Sample data" tag keeps every mock honest (no fabricated evidence).
 */
export function MockCard({
  label,
  children,
  className = "",
}: {
  label: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`rounded-md border border-marketing-border bg-marketing-bg-elevated p-4 shadow-[6px_6px_0_0_hsl(var(--marketing-accent)/0.18)] motion-safe:transition-transform motion-safe:hover:-translate-y-0.5 ${className}`}
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <MonoLabel className="text-marketing-fg">{label}</MonoLabel>
        <MonoLabel>Sample data</MonoLabel>
      </div>
      {children}
    </div>
  );
}
```

`components/CountUp.tsx`:

```tsx
"use client";

import { useEffect, useRef } from "react";
import { animate, useInView, useReducedMotion } from "framer-motion";

const format = (n: number) => n.toLocaleString("en-US");

/**
 * Counts up once when scrolled into view. The initial markup is the final value so no-JS, SSR and
 * reduced-motion users always see the real number; animation only rewrites text content in place.
 */
export function CountUp({ value, className }: { value: number; className?: string }) {
  const ref = useRef<HTMLSpanElement>(null);
  const reduced = useReducedMotion();
  const inView = useInView(ref, { once: true });

  useEffect(() => {
    const node = ref.current;
    if (reduced || !inView || !node) return;
    const controls = animate(0, value, {
      duration: 0.9,
      ease: "easeOut",
      onUpdate: (latest) => {
        node.textContent = format(Math.round(latest));
      },
    });
    return () => controls.stop();
  }, [inView, reduced, value]);

  return (
    <span ref={ref} className={className}>
      {format(value)}
    </span>
  );
}
```

`components/Reveal.tsx`:

```tsx
"use client";

import type { ReactNode } from "react";
import { motion } from "framer-motion";
import { useRevealVariants } from "../lib/motionVariants";

/** Fades/raises its children in once on scroll; fully visible and static under reduced motion. */
export function Reveal({ children, className }: { children: ReactNode; className?: string }) {
  const { item } = useRevealVariants();
  return (
    <motion.div
      variants={item}
      initial="hidden"
      whileInView="visible"
      viewport={{ once: true, amount: 0.15 }}
      className={className}
    >
      {children}
    </motion.div>
  );
}
```

`components/Checkpoint.tsx`:

```tsx
"use client";

import { useRef } from "react";
import { useInView, useReducedMotion } from "framer-motion";

/** Numbered node pinned to the thread rail; lights up while its section is mid-viewport. */
export function Checkpoint({ index }: { index: number }) {
  const ref = useRef<HTMLSpanElement>(null);
  const reduced = useReducedMotion();
  const inView = useInView(ref, { margin: "-40% 0px -40% 0px" });
  const active = Boolean(reduced) || inView;

  return (
    <span
      ref={ref}
      data-testid="checkpoint"
      data-active={active}
      aria-hidden="true"
      className={`absolute left-[1.75rem] top-20 z-10 flex h-7 w-7 -translate-x-1/2 items-center justify-center rounded-sm border bg-marketing-bg font-mono text-[0.625rem] font-medium transition-colors duration-300 md:left-[2.75rem] md:top-28 md:h-8 md:w-8 md:text-[0.6875rem] ${
        active
          ? "border-marketing-accent text-marketing-accent"
          : "border-marketing-border text-marketing-muted-fg"
      }`}
    >
      {String(index).padStart(2, "0")}
    </span>
  );
}
```

`components/ThreadLayout.tsx`:

```tsx
"use client";

import { useRef, type ReactNode } from "react";
import { motion, useReducedMotion, useScroll, useTransform } from "framer-motion";

/**
 * Wraps the checkpoint sections with one continuous vertical rail. The accent overlay's scaleY is
 * scroll-linked (no measured coordinates, so it survives any section height/width); under reduced
 * motion the rail renders fully drawn. Purely decorative, hence aria-hidden.
 */
export function ThreadLayout({ children }: { children: ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const reduced = useReducedMotion();
  const { scrollYProgress } = useScroll({ target: ref, offset: ["start 75%", "end 55%"] });
  const scaleY = useTransform(scrollYProgress, [0, 1], [0, 1]);

  return (
    <div ref={ref} className="relative">
      <div
        aria-hidden="true"
        data-testid="thread"
        className="pointer-events-none absolute inset-y-0 left-1/2 w-full max-w-6xl -translate-x-1/2"
      >
        <span className="absolute inset-y-0 left-[1.75rem] w-px bg-marketing-border/50 md:left-[2.75rem]" />
        <motion.span
          data-testid="thread-progress"
          className="absolute inset-y-0 left-[1.75rem] w-px origin-top bg-marketing-accent md:left-[2.75rem]"
          style={{ scaleY: reduced ? 1 : scaleY }}
        />
      </div>
      {children}
    </div>
  );
}
```

`components/SectionShell.tsx`:

```tsx
import type { ReactNode } from "react";
import { Checkpoint } from "./Checkpoint";
import { MonoLabel } from "./MonoLabel";
import { Reveal } from "./Reveal";

/**
 * One checkpoint section on the custody thread. The "elevated" tone fades a raised band in and out
 * (gradient, not a hard divider) so adjacent sections transition instead of stacking.
 */
export function SectionShell({
  id,
  index,
  eyebrow,
  title,
  lede,
  tone = "base",
  children,
}: {
  id: string;
  index: number;
  eyebrow: string;
  title: ReactNode;
  lede?: ReactNode;
  tone?: "base" | "elevated";
  children: ReactNode;
}) {
  return (
    <section
      id={id}
      aria-labelledby={`${id}-heading`}
      className={tone === "elevated" ? "bg-gradient-to-b from-transparent via-marketing-bg-elevated/70 to-transparent" : ""}
    >
      <div className="relative mx-auto max-w-6xl px-4 py-20 pl-14 md:py-28 md:pl-24">
        <Checkpoint index={index} />
        <MonoLabel>
          {String(index).padStart(2, "0")} — {eyebrow}
        </MonoLabel>
        <h2
          id={`${id}-heading`}
          className="mt-3 max-w-3xl font-display text-3xl font-semibold leading-[1.1] tracking-tight text-marketing-fg sm:text-4xl md:text-5xl"
        >
          {title}
        </h2>
        {lede && <p className="mt-4 max-w-2xl text-base text-marketing-muted-fg md:text-lg">{lede}</p>}
        <Reveal className="mt-10 md:mt-14">{children}</Reveal>
      </div>
    </section>
  );
}
```

`components/MarketingGrid.tsx`:

```tsx
/** Decorative faint waybill grid, radially masked so it fades out — atmosphere for Hero and /track. */
export function MarketingGrid({ className = "" }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      data-testid="marketing-grid"
      className={`pointer-events-none absolute inset-0 -z-10 ${className}`}
      style={{
        backgroundImage:
          "linear-gradient(hsl(var(--marketing-border) / 0.18) 1px, transparent 1px), linear-gradient(90deg, hsl(var(--marketing-border) / 0.18) 1px, transparent 1px)",
        backgroundSize: "48px 48px",
        maskImage: "radial-gradient(ellipse 80% 70% at 60% 30%, black 20%, transparent 75%)",
        WebkitMaskImage: "radial-gradient(ellipse 80% 70% at 60% 30%, black 20%, transparent 75%)",
      }}
    />
  );
}
```

- [ ] **Step 9: Run tests to verify pass**

Run: `npx vitest run src/features/marketing`
Expected: new tests PASS. (Existing marketing tests unaffected so far.) If `ThreadLayout`'s transform assertion fails because framer renders `scaleY` differently, assert `style.transform` contains `scale`/`1` after inspecting the actual DOM — keep the intent: reduced motion ⇒ fully drawn.

- [ ] **Step 10: Commit**

```bash
git add src/web/src/features/marketing src/web/src/app/globals.css
git commit -m "feat(web): add custody-thread primitives and diagram math helpers

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Content data and the three diagrams

**Files:**
- Modify: `src/web/src/features/marketing/content.ts` (append; change `NAV_LINKS` and the `WalkthroughTone` usage stays), `content.test.ts`
- Create: `components/StageList.tsx`, `CustodyChainDiagram.tsx`, `CodWaterfallDiagram.tsx`, `ManifestReconcileDiagram.tsx` + tests for each

**Interfaces:**
- Consumes: `codBreakdown`, `reconcileManifest`, `formatNpr` (Task 1), `MonoLabel`.
- Produces (content.ts): `CUSTODY_CHAIN: CustodyStep[]`, `PROMISE_ITEMS`, `SCAN_OPERATIONS: string[]`, `SCAN_CHECKS: string[]`, `COD_STAGES: string[]`, `NDR_STAGES: string[]`, `RTO_STAGES: string[]`, `DELIVERY_FAILURE_REASONS: string[]`, `PERMISSION_SCOPE_EXAMPLE`, `MANIFEST_SAMPLE`; new `NAV_LINKS`.
- Produces (components): `<StageList label: string stages: string[] />`, `<CustodyChainDiagram />`, `<CodWaterfallDiagram collected?: number fee?: number />`, `<ManifestReconcileDiagram />`.

- [ ] **Step 1: Write failing tests**

`components/CustodyChainDiagram.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CustodyChainDiagram } from "./CustodyChainDiagram";
import { CUSTODY_CHAIN } from "../content";

describe("CustodyChainDiagram", () => {
  it("is a labelled figure exposing every step as text for assistive tech", () => {
    render(<CustodyChainDiagram />);
    const figure = screen.getByRole("figure", { name: /custody chain/i });
    expect(figure).toBeInTheDocument();
    for (const step of CUSTODY_CHAIN) {
      expect(screen.getAllByText(step.label).length).toBeGreaterThan(0);
    }
  });
  it("keeps the decorative SVGs out of the accessibility tree", () => {
    const { container } = render(<CustodyChainDiagram />);
    for (const svg of container.querySelectorAll("svg")) {
      expect(svg).toHaveAttribute("aria-hidden", "true");
    }
  });
  it("marks the chain as sample/illustrative flow with a legend", () => {
    render(<CustodyChainDiagram />);
    expect(screen.getByText(/scan-verified/i)).toBeInTheDocument();
  });
});
```

`components/CodWaterfallDiagram.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CodWaterfallDiagram } from "./CodWaterfallDiagram";

describe("CodWaterfallDiagram", () => {
  it("shows collected, fee and merchant payable as text (2,500 − 150 = 2,350)", () => {
    render(<CodWaterfallDiagram />);
    const figure = screen.getByRole("figure", { name: /cod/i });
    expect(figure).toHaveTextContent("NPR 2,500");
    expect(figure).toHaveTextContent("NPR 150");
    expect(figure).toHaveTextContent("NPR 2,350");
  });
  it("derives payable from props rather than hardcoding it", () => {
    render(<CodWaterfallDiagram collected={1000} fee={100} />);
    expect(screen.getByRole("figure", { name: /cod/i })).toHaveTextContent("NPR 900");
  });
  it("discloses sample data", () => {
    render(<CodWaterfallDiagram />);
    expect(screen.getByText(/sample data/i)).toBeInTheDocument();
  });
});
```

`components/ManifestReconcileDiagram.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ManifestReconcileDiagram } from "./ManifestReconcileDiagram";
import { MANIFEST_SAMPLE } from "../content";

describe("ManifestReconcileDiagram", () => {
  it("states expected, scanned and missing counts as text", () => {
    render(<ManifestReconcileDiagram />);
    const figure = screen.getByRole("figure", { name: /manifest/i });
    expect(figure).toHaveTextContent(`Expected ${MANIFEST_SAMPLE.expected}`);
    expect(figure).toHaveTextContent(`Scanned ${MANIFEST_SAMPLE.expected - MANIFEST_SAMPLE.missingIndices.length}`);
    expect(figure).toHaveTextContent(`Missing ${MANIFEST_SAMPLE.missingIndices.length}`);
  });
  it("draws one cell per expected shipment and flags missing ones without relying on color alone", () => {
    const { container } = render(<ManifestReconcileDiagram />);
    expect(container.querySelectorAll("[data-cell]")).toHaveLength(MANIFEST_SAMPLE.expected);
    expect(container.querySelectorAll("[data-cell-missing]")).toHaveLength(MANIFEST_SAMPLE.missingIndices.length);
    expect(container.querySelectorAll("[data-cell-missing-mark]").length).toBeGreaterThan(0);
  });
});
```

`components/StageList.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StageList } from "./StageList";

describe("StageList", () => {
  it("renders a labelled ordered list of stages", () => {
    render(<StageList label="NDR workflow" stages={["Open", "Contacting", "Resolved"]} />);
    expect(screen.getByRole("list", { name: "NDR workflow" })).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
  });
});
```

Add to `content.test.ts` (keep existing tests; update any that assert old `NAV_LINKS` anchors to the new values):

```ts
import { CUSTODY_CHAIN, COD_STAGES, NDR_STAGES, RTO_STAGES, MANIFEST_SAMPLE, NAV_LINKS } from "./content";

describe("redesign content", () => {
  it("models the 8-step shipment lifecycle with evidence types", () => {
    expect(CUSTODY_CHAIN).toHaveLength(8);
    expect(CUSTODY_CHAIN[0].evidence).toBe("booking");
    expect(CUSTODY_CHAIN[7].evidence).toBe("proof");
  });
  it("matches the COD (7), NDR (6) and RTO (4) lifecycles from the spec", () => {
    expect(COD_STAGES).toHaveLength(7);
    expect(NDR_STAGES).toHaveLength(6);
    expect(RTO_STAGES).toHaveLength(4);
  });
  it("keeps manifest sample indices inside the expected range", () => {
    for (const index of MANIFEST_SAMPLE.missingIndices) {
      expect(index).toBeLessThan(MANIFEST_SAMPLE.expected);
    }
  });
  it("uses root-relative anchors so nav works from /pricing too", () => {
    expect(NAV_LINKS.filter((l) => l.href.includes("#")).every((l) => l.href.startsWith("/#"))).toBe(true);
    expect(NAV_LINKS.some((l) => l.href === "/pricing")).toBe(true);
    expect(NAV_LINKS.some((l) => l.href === "/track")).toBe(true);
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/features/marketing`
Expected: FAIL (missing exports/modules).

- [ ] **Step 3: Update `content.ts`**

Replace the `NAV_LINKS` array with:

```ts
export const NAV_LINKS: NavLink[] = [
  { href: "/#custody", label: "How it works" },
  { href: "/#cod", label: "COD" },
  { href: "/pricing", label: "Pricing" },
  { href: "/track", label: "Track a parcel" },
];
```

Append to the end of the file:

```ts
export type CustodyEvidence = "booking" | "scan" | "proof";

export interface CustodyStep {
  label: string;
  holder: string;
  evidence: CustodyEvidence;
  evidenceLabel: string;
}

// The happy-path lifecycle from CLAUDE.md §10, annotated with the physical evidence behind each hop
// (§12–13: a status change alone is never a physical transfer).
export const CUSTODY_CHAIN: CustodyStep[] = [
  { label: "Created", holder: "Counter or merchant", evidence: "booking", evidenceLabel: "BOOKING" },
  { label: "Picked up", holder: "Pickup rider", evidence: "scan", evidenceLabel: "SCAN · PICKUP" },
  { label: "At origin", holder: "Origin branch", evidence: "scan", evidenceLabel: "SCAN · RECEIVE" },
  { label: "Sorted", holder: "Origin hub", evidence: "scan", evidenceLabel: "SCAN · SORT" },
  { label: "In transit", holder: "Manifest driver", evidence: "scan", evidenceLabel: "SCAN · DISPATCH" },
  { label: "At destination", holder: "Destination branch", evidence: "scan", evidenceLabel: "SCAN · RECEIVE" },
  { label: "Out for delivery", holder: "Delivery rider", evidence: "scan", evidenceLabel: "SCAN · DELIVERY" },
  { label: "Delivered", holder: "Recipient", evidence: "proof", evidenceLabel: "PROOF · POD" },
];

export interface PromiseItem {
  numeral: string;
  title: string;
  body: string;
}

export const PROMISE_ITEMS: PromiseItem[] = [
  { numeral: "01", title: "Custody, not just status", body: "A parcel only moves when a real scan says it did." },
  {
    numeral: "02",
    title: "Every rupee traceable",
    body: "COD from collection to merchant payout, with before and after on every adjustment.",
  },
  {
    numeral: "03",
    title: "Built for Nepal",
    body: "Province, district, local level and ward drive delivery zones and rates.",
  },
];

export const SCAN_OPERATIONS: string[] = [
  "Receive",
  "Sort",
  "Dispatch",
  "Transfer",
  "Rider pickup",
  "Rider delivery",
  "RTO",
];

export const SCAN_CHECKS: string[] = [
  "User permission",
  "Branch access",
  "Shipment state",
  "Expected operation",
  "Shipment custody",
];

export const COD_STAGES: string[] = [
  "Expected",
  "Collected",
  "Rider deposited",
  "Branch reconciled",
  "Finance verified",
  "Settlement created",
  "Settled",
];

export const NDR_STAGES: string[] = [
  "Open",
  "Contacting",
  "Customer confirmed",
  "Reattempt scheduled",
  "RTO requested",
  "Resolved",
];

export const RTO_STAGES: string[] = ["RTO initiated", "RTO in transit", "RTO at origin", "RTO delivered"];

export const DELIVERY_FAILURE_REASONS: string[] = [
  "Customer unavailable",
  "Phone unreachable",
  "Wrong address",
  "Customer refused",
  "Cash unavailable",
];

export const PERMISSION_SCOPE_EXAMPLE = {
  role: "Branch Manager",
  branch: "Kathmandu Branch",
  permissions: ["shipment.read", "shipment.assign", "shipment.scan"],
} as const;

// Fictional sample manifest — labelled "Sample data" everywhere it renders.
export const MANIFEST_SAMPLE = {
  code: "MNF-20260818-001",
  from: "Kathmandu Hub",
  to: "Pokhara Hub",
  expected: 120,
  missingIndices: [87],
} as const;
```

- [ ] **Step 4: Implement `StageList.tsx`**

```tsx
/** Vertical rail of ordered lifecycle stages (COD / NDR / RTO); the rail echoes the page's thread. */
export function StageList({ label, stages }: { label: string; stages: string[] }) {
  return (
    <ol aria-label={label} className="relative flex flex-col gap-3 border-l border-marketing-border pl-5">
      {stages.map((stage, index) => (
        <li
          key={stage}
          className="relative font-mono text-xs uppercase tracking-[0.12em] text-marketing-muted-fg transition-colors hover:text-marketing-fg"
        >
          <span
            aria-hidden="true"
            className="absolute -left-[1.6875rem] top-1/2 h-2 w-2 -translate-y-1/2 rounded-sm bg-marketing-accent"
          />
          <span className="mr-2 text-marketing-accent">{String(index + 1).padStart(2, "0")}</span>
          {stage}
        </li>
      ))}
    </ol>
  );
}
```

- [ ] **Step 5: Implement `CustodyChainDiagram.tsx`**

```tsx
import { CUSTODY_CHAIN } from "../content";
import { MonoLabel } from "./MonoLabel";

const ACCENT = "hsl(var(--marketing-accent))";
const BORDER = "hsl(var(--marketing-border))";

function Node({ x, y, evidence }: { x: number; y: number; evidence: string }) {
  // Hollow = booking (no physical evidence yet); filled = physical scan or proof of delivery.
  return evidence === "booking" ? (
    <circle cx={x} cy={y} r={8} fill="hsl(var(--marketing-bg))" stroke={ACCENT} strokeWidth={2} />
  ) : (
    <circle cx={x} cy={y} r={8} fill={ACCENT} />
  );
}

/**
 * The 8-step lifecycle as a chain of custody. Horizontal from lg up, vertical below (the 8-column
 * layout is unreadable at tablet widths). Both SVGs are decorative — the sr-only list carries the content.
 */
export function CustodyChainDiagram() {
  const n = CUSTODY_CHAIN.length;
  const stepX = 120;
  const x0 = 60;
  const stepY = 64;

  return (
    <figure aria-label="Custody chain: eight lifecycle steps from booking to proof of delivery" className="flex flex-col gap-6">
      <svg
        aria-hidden="true"
        viewBox="0 0 960 130"
        className="hidden h-auto w-full lg:block"
        fontFamily="var(--font-plex-sans), sans-serif"
      >
        <line x1={x0} y1={40} x2={x0 + stepX * (n - 1)} y2={40} stroke={BORDER} strokeWidth={2} />
        {CUSTODY_CHAIN.map((step, i) => {
          const x = x0 + i * stepX;
          return (
            <g key={step.label}>
              <Node x={x} y={40} evidence={step.evidence} />
              <text x={x} y={72} textAnchor="middle" fontSize={13} fontWeight={600} className="fill-marketing-fg">
                {step.label}
              </text>
              <text x={x} y={90} textAnchor="middle" fontSize={12} className="fill-marketing-muted-fg">
                {step.holder}
              </text>
              <text
                x={x}
                y={112}
                textAnchor="middle"
                fontSize={10}
                letterSpacing={1}
                fontFamily="var(--font-plex-mono), monospace"
                className="fill-marketing-accent"
              >
                {step.evidenceLabel}
              </text>
            </g>
          );
        })}
      </svg>

      <svg
        aria-hidden="true"
        viewBox={`0 0 320 ${n * stepY}`}
        className="mx-auto h-auto w-full max-w-sm lg:hidden"
        fontFamily="var(--font-plex-sans), sans-serif"
      >
        <line x1={20} y1={32} x2={20} y2={32 + stepY * (n - 1)} stroke={BORDER} strokeWidth={2} />
        {CUSTODY_CHAIN.map((step, i) => {
          const y = 32 + i * stepY;
          return (
            <g key={step.label}>
              <Node x={20} y={y} evidence={step.evidence} />
              <text x={44} y={y - 2} fontSize={15} fontWeight={600} className="fill-marketing-fg">
                {step.label}
              </text>
              <text x={44} y={y + 15} fontSize={12} className="fill-marketing-muted-fg">
                {step.holder}
              </text>
              <text
                x={44}
                y={y + 30}
                fontSize={10}
                letterSpacing={1}
                fontFamily="var(--font-plex-mono), monospace"
                className="fill-marketing-accent"
              >
                {step.evidenceLabel}
              </text>
            </g>
          );
        })}
      </svg>

      <ol className="sr-only">
        {CUSTODY_CHAIN.map((step) => (
          <li key={step.label}>
            {step.label} — held by {step.holder} ({step.evidenceLabel})
          </li>
        ))}
      </ol>

      <figcaption className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <MonoLabel>Filled node = scan-verified or proof of delivery</MonoLabel>
        <MonoLabel>Hollow node = booking, no physical evidence yet</MonoLabel>
      </figcaption>
    </figure>
  );
}
```

- [ ] **Step 6: Implement `CodWaterfallDiagram.tsx`**

```tsx
import { codBreakdown, formatNpr } from "../lib/diagramMath";
import { MonoLabel } from "./MonoLabel";

const H = 140;
const BASE = 180;

/** COD waterfall: collected − courier fee = merchant payable. Every bar is computed from the inputs. */
export function CodWaterfallDiagram({ collected = 2500, fee = 150 }: { collected?: number; fee?: number }) {
  const { payable } = codBreakdown(collected, fee);
  const scale = H / collected;
  const payableH = payable * scale;
  const feeH = H - payableH;

  const bars = [
    { x: 10, y: BASE - H, h: H, fill: "hsl(var(--marketing-accent))", lines: ["Collected"], value: formatNpr(collected) },
    {
      x: 140,
      y: BASE - H,
      h: feeH,
      fill: "hsl(var(--marketing-tone-neutral))",
      lines: ["Courier", "fee"],
      value: `− ${formatNpr(fee)}`,
    },
    {
      x: 270,
      y: BASE - payableH,
      h: payableH,
      fill: "hsl(var(--marketing-tone-success))",
      lines: ["Merchant", "payable"],
      value: formatNpr(payable),
    },
  ];

  return (
    <figure
      aria-label={`COD waterfall: ${formatNpr(collected)} collected minus ${formatNpr(fee)} courier fee equals ${formatNpr(payable)} merchant payable`}
      className="flex flex-col gap-4"
    >
      <svg
        aria-hidden="true"
        viewBox="0 0 360 232"
        className="h-auto w-full max-w-md"
        fontFamily="var(--font-plex-mono), monospace"
      >
        <line x1={0} y1={BASE} x2={360} y2={BASE} stroke="hsl(var(--marketing-border))" strokeWidth={1} />
        <line x1={90} y1={BASE - H} x2={140} y2={BASE - H} stroke="hsl(var(--marketing-border))" strokeDasharray="3 4" />
        <line x1={220} y1={BASE - payableH} x2={270} y2={BASE - payableH} stroke="hsl(var(--marketing-border))" strokeDasharray="3 4" />
        {bars.map((bar) => (
          <g key={bar.lines.join(" ")}>
            <rect x={bar.x} y={bar.y} width={80} height={Math.max(bar.h, 2)} rx={2} fill={bar.fill} />
            <text x={bar.x + 40} y={bar.y - 8} textAnchor="middle" fontSize={14} fontWeight={600} className="fill-marketing-fg">
              {bar.value}
            </text>
            {bar.lines.map((line, i) => (
              <text
                key={line}
                x={bar.x + 40}
                y={BASE + 20 + i * 16}
                textAnchor="middle"
                fontSize={13}
                fontFamily="var(--font-plex-sans), sans-serif"
                className="fill-marketing-muted-fg"
              >
                {line}
              </text>
            ))}
          </g>
        ))}
      </svg>
      <p className="sr-only">
        {formatNpr(collected)} collected, minus {formatNpr(fee)} courier fee, equals {formatNpr(payable)} merchant
        payable.
      </p>
      <figcaption>
        <MonoLabel>Sample data — illustrative amounts</MonoLabel>
      </figcaption>
    </figure>
  );
}
```

Note: the test asserts `figure` has text `NPR 2,500` etc. — the sr-only paragraph provides it in the DOM text. The visible SVG text is also present.

- [ ] **Step 7: Implement `ManifestReconcileDiagram.tsx`**

```tsx
import { MANIFEST_SAMPLE } from "../content";
import { reconcileManifest } from "../lib/diagramMath";
import { MonoLabel } from "./MonoLabel";

const COLS = 12;
const PITCH = 30;
const CELL = 26;

/** Grid of expected shipments; each scanned cell is filled, each missing one is outlined and X-marked. */
export function ManifestReconcileDiagram() {
  const { code, from, to, expected, missingIndices } = MANIFEST_SAMPLE;
  const result = reconcileManifest(expected, [...missingIndices]);
  const missing = new Set(result.missingIndices);
  const rows = Math.ceil(expected / COLS);

  return (
    <figure
      aria-label={`Manifest ${code} from ${from} to ${to}: ${result.scanned} of ${result.expected} shipments scanned, ${result.missing} missing`}
      className="flex flex-col gap-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <MonoLabel className="text-marketing-fg">
          {code} · {from} → {to}
        </MonoLabel>
        <MonoLabel>Sample data</MonoLabel>
      </div>
      <svg
        aria-hidden="true"
        viewBox={`0 0 ${COLS * PITCH - (PITCH - CELL)} ${rows * PITCH - (PITCH - CELL)}`}
        className="h-auto w-full max-w-md"
      >
        {Array.from({ length: expected }, (_, i) => {
          const x = (i % COLS) * PITCH;
          const y = Math.floor(i / COLS) * PITCH;
          if (missing.has(i)) {
            return (
              <g key={i} data-cell data-cell-missing>
                <rect
                  x={x + 1}
                  y={y + 1}
                  width={CELL - 2}
                  height={CELL - 2}
                  rx={2}
                  fill="none"
                  stroke="hsl(var(--marketing-tone-alert))"
                  strokeWidth={2}
                />
                <path
                  data-cell-missing-mark
                  d={`M${x + 7},${y + 7} L${x + CELL - 7},${y + CELL - 7} M${x + CELL - 7},${y + 7} L${x + 7},${y + CELL - 7}`}
                  stroke="hsl(var(--marketing-tone-alert))"
                  strokeWidth={2}
                  strokeLinecap="round"
                />
              </g>
            );
          }
          return (
            <rect key={i} data-cell x={x} y={y} width={CELL} height={CELL} rx={2} fill="hsl(var(--marketing-accent))" fillOpacity={0.85} />
          );
        })}
      </svg>
      <figcaption className="flex flex-wrap items-center gap-x-6 gap-y-1">
        <MonoLabel className="text-marketing-fg">Expected {result.expected}</MonoLabel>
        <MonoLabel className="text-marketing-fg">Scanned {result.scanned}</MonoLabel>
        <MonoLabel className="text-marketing-fg">Missing {result.missing}</MonoLabel>
      </figcaption>
    </figure>
  );
}
```

- [ ] **Step 8: Run tests**

Run: `npx vitest run src/features/marketing`
Expected: new tests and `content.test.ts` PASS. Existing component tests that referenced old `NAV_LINKS` anchors may fail — update only those assertions to the new hrefs.

- [ ] **Step 9: Commit**

```bash
git add src/web/src/features/marketing
git commit -m "feat(web): add marketing content data, StageList, and custody/COD/manifest diagrams

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Editorial Hero and Promise strip

**Files:**
- Modify: `components/Hero.tsx`, `components/Hero.test.tsx`
- Create: `components/PromiseStrip.tsx`, `components/PromiseStrip.test.tsx`

**Interfaces:**
- Consumes: `MonoLabel`, `MockCard`, `ScanBar`, `CountUp`, `MarketingGrid`, `AppScreenshotFrame`, `codBreakdown`, `formatNpr`, `getPrimaryCta`, `SCREENSHOTS`, `PROMISE_ITEMS`, `MANIFEST_SAMPLE`, `useRevealVariants`.
- Produces: `<Hero isAuthenticated: boolean />` (same signature), `<PromiseStrip />`.

- [ ] **Step 1: Rewrite `Hero.test.tsx`**

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

  it("links the primary CTA to /register when unauthenticated", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /get started/i })).toHaveAttribute("href", "/register");
  });

  it("links the primary CTA to /dashboard when authenticated", () => {
    render(<Hero isAuthenticated />);
    expect(screen.getByRole("link", { name: /go to dashboard/i })).toHaveAttribute("href", "/dashboard");
  });

  it("links the secondary CTA to the custody section anchor", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "See how it works" })).toHaveAttribute("href", "#custody");
  });

  it("shows the real dashboard screenshot as the hero visual", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("img", { name: dashboardScreenshot.alt })).toHaveAttribute("src", dashboardScreenshot.src);
  });

  it("layers labelled sample-data cards over the screenshot", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getAllByText("Sample data").length).toBeGreaterThanOrEqual(3);
    expect(screen.getByText(/NPR 2,350/)).toBeInTheDocument();
  });

  it("renders the decorative grid background", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByTestId("marketing-grid")).toHaveAttribute("aria-hidden", "true");
  });
});
```

`components/PromiseStrip.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { PromiseStrip } from "./PromiseStrip";
import { PROMISE_ITEMS } from "../content";

describe("PromiseStrip", () => {
  it("renders every promise as a list item without adding headings (keeps heading order clean)", () => {
    render(<PromiseStrip />);
    expect(screen.getAllByRole("listitem")).toHaveLength(PROMISE_ITEMS.length);
    expect(screen.queryAllByRole("heading")).toHaveLength(0);
    for (const item of PROMISE_ITEMS) {
      expect(screen.getByText(item.title)).toBeInTheDocument();
      expect(screen.getByText(item.body)).toBeInTheDocument();
    }
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/features/marketing/components/Hero.test.tsx src/features/marketing/components/PromiseStrip.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement `PromiseStrip.tsx`**

```tsx
import { PROMISE_ITEMS } from "../content";
import { MonoLabel } from "./MonoLabel";

/** Three-claim strip between Hero and the thread — the visual hand-off from atmosphere to structure. */
export function PromiseStrip() {
  return (
    <div className="border-y border-marketing-border/60 bg-marketing-bg-elevated/50">
      <ul className="mx-auto grid max-w-6xl divide-y divide-marketing-border/60 px-4 md:grid-cols-3 md:divide-x md:divide-y-0">
        {PROMISE_ITEMS.map((item) => (
          <li key={item.numeral} className="flex flex-col gap-2 py-6 md:px-8 md:first:pl-0 md:last:pr-0">
            <MonoLabel className="text-marketing-accent">{item.numeral}</MonoLabel>
            <p className="font-display text-lg font-semibold text-marketing-fg">{item.title}</p>
            <p className="text-sm text-marketing-muted-fg">{item.body}</p>
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 4: Rewrite `Hero.tsx`**

```tsx
"use client";

import Link from "next/link";
import { motion } from "framer-motion";
import { ArrowRight } from "lucide-react";
import { MANIFEST_SAMPLE, getPrimaryCta, SCREENSHOTS } from "../content";
import { codBreakdown, formatNpr } from "../lib/diagramMath";
import { useRevealVariants } from "../lib/motionVariants";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { CountUp } from "./CountUp";
import { MarketingGrid } from "./MarketingGrid";
import { MockCard, ScanBar } from "./MockCard";
import { MonoLabel } from "./MonoLabel";

const dashboardScreenshot = SCREENSHOTS.find((s) => s.id === "dashboard")!;
const cod = codBreakdown(2500, 150);
const manifestScanned = MANIFEST_SAMPLE.expected - MANIFEST_SAMPLE.missingIndices.length;

export function Hero({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);
  const { container, item } = useRevealVariants();

  return (
    <section className="relative w-full overflow-hidden">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 -z-20"
        style={{
          background:
            "radial-gradient(ellipse 60% 50% at 80% 20%, hsl(var(--marketing-accent) / 0.12), transparent 70%)",
        }}
      />
      <MarketingGrid />
      <motion.div
        variants={container}
        initial="hidden"
        animate="visible"
        className="relative mx-auto max-w-6xl px-4 pb-24 pt-16 md:pb-32 md:pt-24"
      >
        <motion.div variants={item} className="flex flex-wrap items-center gap-x-6 gap-y-2">
          <MonoLabel className="text-marketing-accent">Nepal domestic courier operations</MonoLabel>
          <MonoLabel>AWB NP2026-000000 · KTM → PKR · In transit</MonoLabel>
        </motion.div>

        <motion.h1
          variants={item}
          className="mt-6 max-w-5xl font-display text-[clamp(2.75rem,8vw,6.5rem)] font-semibold leading-[0.95] tracking-tight text-marketing-fg"
        >
          Every parcel, accounted for.
        </motion.h1>

        <div className="mt-8 grid gap-8 md:grid-cols-[1.2fr_1fr] md:items-end">
          <motion.p variants={item} className="max-w-xl text-lg text-marketing-muted-fg">
            Scan-driven custody, hub transfers, delivery attempts, and COD settlement — one platform that always
            knows where a parcel is, who has it, and what happens next.
          </motion.p>
          <motion.div variants={item} className="flex flex-wrap items-center gap-3 md:justify-end">
            <Link
              href={cta.href}
              className="group inline-flex items-center gap-2 rounded-md bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg shadow-[4px_4px_0_0_hsl(var(--marketing-fg)/0.18)] transition-transform hover:-translate-y-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            >
              {cta.label}
              <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
            </Link>
            <a
              href="#custody"
              className="rounded-md border border-marketing-border px-6 py-3 text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            >
              See how it works
            </a>
          </motion.div>
        </div>

        <motion.div variants={item} className="relative mt-14 md:mt-20">
          <div className="md:ml-16">
            <AppScreenshotFrame src={dashboardScreenshot.src} alt={dashboardScreenshot.alt} hoverTilt priority />
          </div>
          <div className="mt-6 grid gap-4 sm:grid-cols-3 md:contents">
            <MockCard label="Scan received" className="md:absolute md:-left-2 md:top-[52%] md:w-64">
              <p className="font-mono text-sm text-marketing-fg">NP2026-000000</p>
              <p className="mt-1 text-sm text-marketing-muted-fg">Received · Kathmandu Hub</p>
              <div className="mt-3">
                <ScanBar />
              </div>
            </MockCard>
            <MockCard label="COD ledger" className="md:absolute md:-bottom-10 md:right-4 md:w-72">
              <dl className="flex flex-col gap-1 text-sm">
                <div className="flex justify-between gap-4">
                  <dt className="text-marketing-muted-fg">Collected</dt>
                  <dd className="font-mono text-marketing-fg">NPR <CountUp value={cod.collected} /></dd>
                </div>
                <div className="flex justify-between gap-4">
                  <dt className="text-marketing-muted-fg">Courier fee</dt>
                  <dd className="font-mono text-marketing-fg">− {formatNpr(cod.fee)}</dd>
                </div>
                <div className="flex justify-between gap-4 border-t border-marketing-border pt-1">
                  <dt className="font-medium text-marketing-fg">Merchant payable</dt>
                  <dd className="font-mono font-medium text-marketing-fg">{formatNpr(cod.payable)}</dd>
                </div>
              </dl>
            </MockCard>
            <MockCard label="Manifest" className="md:absolute md:-top-8 md:right-8 md:w-60">
              <p className="font-mono text-xs text-marketing-fg">{MANIFEST_SAMPLE.code}</p>
              <p className="mt-1 text-sm text-marketing-muted-fg">
                <CountUp value={manifestScanned} /> / {MANIFEST_SAMPLE.expected} scanned ·{" "}
                {MANIFEST_SAMPLE.missingIndices.length} missing
              </p>
            </MockCard>
          </div>
        </motion.div>
      </motion.div>
    </section>
  );
}
```

Note the `NPR <CountUp …/>` split means `getByText(/NPR 2,350/)` matches the payable line only (`NPR 2,350` is one text node from `formatNpr`), which is what the test asserts.

- [ ] **Step 5: Run tests**

Run: `npx vitest run src/features/marketing/components/Hero.test.tsx src/features/marketing/components/PromiseStrip.test.tsx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/features/marketing
git commit -m "feat(web): editorial hero with layered sample cards and promise strip

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Thread sections 01–05, page composition, removal of superseded sections

**Files:**
- Create: `components/CustodySection.tsx`, `ScanSection.tsx`, `CodSection.tsx`, `ExceptionsSection.tsx`, `RolesSection.tsx` + a test for each
- Modify: `src/web/src/app/page.tsx`, `src/web/src/app/page.test.tsx` (if assertions reference removed sections)
- Delete: `HowItWorks.tsx`, `FeatureGrid.tsx`, `ProductWalkthrough.tsx`, `Credibility.tsx` and their tests; delete `RouteLineGraphic.tsx`/`LifecycleRail.tsx` (+ tests) only if `grep -rn "RouteLineGraphic\|LifecycleRail" src` shows no importer outside those files/tests. Remove the now-unused `HOW_IT_WORKS_STEPS`, `FEATURES`, `WALKTHROUGH_STOPS` from `content.ts` **only if** grep shows no other importer (`/track/[awb]/page.tsx` imports `WALKTHROUGH_STOPS` and `LifecycleRail` — keep both).

**Interfaces:**
- Consumes: `SectionShell`, `StageList`, three diagrams, `MockCard`, `MonoLabel`, content constants from Task 2, `CREDIBILITY_POINTS`.
- Produces: `<CustodySection />`, `<ScanSection />`, `<CodSection />`, `<ExceptionsSection />`, `<RolesSection />` (no props).

- [ ] **Step 1: Write failing tests**

One shared pattern; create each file with its own ids:

`components/CustodySection.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CustodySection } from "./CustodySection";

describe("CustodySection", () => {
  it("is the #custody anchor with a labelled h2 and the custody-chain figure", () => {
    const { container } = render(<CustodySection />);
    expect(container.querySelector("section#custody")).not.toBeNull();
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();
    expect(screen.getByRole("figure", { name: /custody chain/i })).toBeInTheDocument();
  });
});
```

`components/ScanSection.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ScanSection } from "./ScanSection";
import { SCAN_CHECKS, SCAN_OPERATIONS } from "../content";

describe("ScanSection", () => {
  it("lists every scan operation and validation check, and shows the manifest reconcile figure", () => {
    const { container } = render(<ScanSection />);
    expect(container.querySelector("section#scan-manifest")).not.toBeNull();
    for (const text of [...SCAN_OPERATIONS, ...SCAN_CHECKS]) {
      expect(screen.getByText(text)).toBeInTheDocument();
    }
    expect(screen.getByRole("figure", { name: /manifest/i })).toBeInTheDocument();
  });
});
```

`components/CodSection.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CodSection } from "./CodSection";
import { COD_STAGES } from "../content";

describe("CodSection", () => {
  it("is the #cod anchor with the waterfall figure and all 7 lifecycle stages", () => {
    const { container } = render(<CodSection />);
    expect(container.querySelector("section#cod")).not.toBeNull();
    expect(screen.getByRole("figure", { name: /cod/i })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: /cod lifecycle/i }).querySelectorAll("li")).toHaveLength(COD_STAGES.length);
  });
});
```

`components/ExceptionsSection.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ExceptionsSection } from "./ExceptionsSection";
import { DELIVERY_FAILURE_REASONS, NDR_STAGES, RTO_STAGES } from "../content";

describe("ExceptionsSection", () => {
  it("shows failure reasons plus the NDR and RTO workflows", () => {
    const { container } = render(<ExceptionsSection />);
    expect(container.querySelector("section#exceptions")).not.toBeNull();
    for (const reason of DELIVERY_FAILURE_REASONS) expect(screen.getByText(reason)).toBeInTheDocument();
    expect(screen.getByRole("list", { name: /ndr workflow/i }).querySelectorAll("li")).toHaveLength(NDR_STAGES.length);
    expect(screen.getByRole("list", { name: /rto workflow/i }).querySelectorAll("li")).toHaveLength(RTO_STAGES.length);
  });
});
```

`components/RolesSection.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RolesSection } from "./RolesSection";
import { CREDIBILITY_POINTS, PERMISSION_SCOPE_EXAMPLE } from "../content";

describe("RolesSection", () => {
  it("shows the permission + branch-scope example and the credibility points", () => {
    const { container } = render(<RolesSection />);
    expect(container.querySelector("section#roles")).not.toBeNull();
    expect(screen.getByText(PERMISSION_SCOPE_EXAMPLE.branch)).toBeInTheDocument();
    for (const permission of PERMISSION_SCOPE_EXAMPLE.permissions) {
      expect(screen.getByText(permission)).toBeInTheDocument();
    }
    for (const point of CREDIBILITY_POINTS) {
      expect(screen.getByText(point.title)).toBeInTheDocument();
    }
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/features/marketing/components/CustodySection.test.tsx src/features/marketing/components/ScanSection.test.tsx src/features/marketing/components/CodSection.test.tsx src/features/marketing/components/ExceptionsSection.test.tsx src/features/marketing/components/RolesSection.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement the five sections**

`components/CustodySection.tsx`:

```tsx
import { CustodyChainDiagram } from "./CustodyChainDiagram";
import { SectionShell } from "./SectionShell";

export function CustodySection() {
  return (
    <SectionShell
      id="custody"
      index={1}
      eyebrow="Custody chain"
      title="Every hand-off is a scan. Every scan is a record."
      lede="A shipment only moves forward through a real operation — a rider pickup, a branch receive, a manifest dispatch, a proof of delivery. Never an arbitrary status edit."
      tone="elevated"
    >
      <CustodyChainDiagram />
    </SectionShell>
  );
}
```

`components/ScanSection.tsx`:

```tsx
import { SCAN_CHECKS, SCAN_OPERATIONS } from "../content";
import { ManifestReconcileDiagram } from "./ManifestReconcileDiagram";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";

export function ScanSection() {
  return (
    <SectionShell
      id="scan-manifest"
      index={2}
      eyebrow="Scan & manifest"
      title="Hubs receive what was sent — and see what wasn't."
      lede="Sealed manifests carry shipments branch to branch. Receiving means scanning each one, so a missing parcel is flagged the moment the truck is unloaded."
    >
      <div className="grid gap-12 lg:grid-cols-2 lg:items-start">
        <div className="flex flex-col gap-8">
          <div className="flex flex-col gap-3">
            <MonoLabel>Scan operations</MonoLabel>
            <ul className="flex flex-wrap gap-2">
              {SCAN_OPERATIONS.map((operation) => (
                <li
                  key={operation}
                  className="rounded-sm border border-marketing-border px-3 py-1.5 font-mono text-xs uppercase tracking-[0.1em] text-marketing-fg transition-colors hover:border-marketing-accent"
                >
                  {operation}
                </li>
              ))}
            </ul>
          </div>
          <div className="flex flex-col gap-3">
            <MonoLabel>Every scan validates</MonoLabel>
            <ul className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
              {SCAN_CHECKS.map((check) => (
                <li key={check} className="py-2.5 text-sm text-marketing-fg">
                  {check}
                </li>
              ))}
            </ul>
          </div>
        </div>
        <ManifestReconcileDiagram />
      </div>
    </SectionShell>
  );
}
```

`components/CodSection.tsx`:

```tsx
import { COD_STAGES } from "../content";
import { CodWaterfallDiagram } from "./CodWaterfallDiagram";
import { SectionShell } from "./SectionShell";
import { StageList } from "./StageList";

export function CodSection() {
  return (
    <SectionShell
      id="cod"
      index={3}
      eyebrow="COD ledger"
      title="Cash on delivery, followed to the last rupee."
      lede="COD is a financial lifecycle, not a checkbox. Collection, rider deposit, branch reconciliation, finance verification and merchant settlement each leave a traceable record."
      tone="elevated"
    >
      <div className="grid gap-12 lg:grid-cols-[1.2fr_1fr] lg:items-center">
        <CodWaterfallDiagram />
        <StageList label="COD lifecycle" stages={COD_STAGES} />
      </div>
    </SectionShell>
  );
}
```

`components/ExceptionsSection.tsx`:

```tsx
import { DELIVERY_FAILURE_REASONS, NDR_STAGES, RTO_STAGES } from "../content";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";
import { StageList } from "./StageList";

export function ExceptionsSection() {
  return (
    <SectionShell
      id="exceptions"
      index={4}
      eyebrow="Exceptions"
      title="A failed delivery is a workflow, not a dead end."
      lede="Every attempt records who, when, where and why. Non-delivery reports route to staff follow-up, and returns keep their link to the original shipment."
    >
      <div className="grid gap-12 md:grid-cols-3">
        <div className="flex flex-col gap-3">
          <MonoLabel>Recorded attempt reasons</MonoLabel>
          <ul className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
            {DELIVERY_FAILURE_REASONS.map((reason) => (
              <li key={reason} className="py-2.5 text-sm text-marketing-fg">
                {reason}
              </li>
            ))}
          </ul>
        </div>
        <div className="flex flex-col gap-4">
          <MonoLabel>NDR</MonoLabel>
          <StageList label="NDR workflow" stages={NDR_STAGES} />
        </div>
        <div className="flex flex-col gap-4">
          <MonoLabel>RTO</MonoLabel>
          <StageList label="RTO workflow" stages={RTO_STAGES} />
        </div>
      </div>
    </SectionShell>
  );
}
```

`components/RolesSection.tsx`:

```tsx
import { CREDIBILITY_POINTS, PERMISSION_SCOPE_EXAMPLE } from "../content";
import { MockCard } from "./MockCard";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";

export function RolesSection() {
  const example = PERMISSION_SCOPE_EXAMPLE;
  return (
    <SectionShell
      id="roles"
      index={5}
      eyebrow="Roles & branches"
      title="Permission plus branch scope. Never one without the other."
      lede="Access is granted by permission and limited to the branches a user actually works in — enforced on the server, so a tampered ID gets nothing."
      tone="elevated"
    >
      <div className="grid gap-12 lg:grid-cols-[1fr_1.2fr] lg:items-start">
        <MockCard label={example.role}>
          <MonoLabel>Permissions</MonoLabel>
          <ul className="mt-2 flex flex-col gap-1.5 font-mono text-sm text-marketing-fg">
            {example.permissions.map((permission) => (
              <li key={permission}>{permission}</li>
            ))}
          </ul>
          <div className="mt-4 border-t border-marketing-border pt-3">
            <MonoLabel>Branch scope</MonoLabel>
            <p className="mt-1 text-sm text-marketing-fg">{example.branch}</p>
          </div>
        </MockCard>
        <div className="flex flex-col gap-3">
          <MonoLabel>What&apos;s already true</MonoLabel>
          <p className="max-w-xl text-sm text-marketing-muted-fg">
            No case studies yet — this is still an early platform. Today, these hold:
          </p>
          <div className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
            {CREDIBILITY_POINTS.map((point) => (
              <div key={point.title} className="flex flex-col gap-1.5 py-5 sm:flex-row sm:gap-6">
                <h3 className="font-display text-base font-semibold text-marketing-fg sm:w-56 sm:shrink-0">
                  {point.title}
                </h3>
                <p className="text-sm text-marketing-muted-fg">{point.description}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </SectionShell>
  );
}
```

- [ ] **Step 4: Run section tests** — Expected: PASS.

- [ ] **Step 5: Recompose `src/app/page.tsx`** (Task 5 will convert `ScreenShowcase` to section 06; import it here now so the page is final):

```tsx
import { hasSession } from "@/lib/auth/session";
import { MarketingNav } from "@/features/marketing/components/MarketingNav";
import { Hero } from "@/features/marketing/components/Hero";
import { PromiseStrip } from "@/features/marketing/components/PromiseStrip";
import { ThreadLayout } from "@/features/marketing/components/ThreadLayout";
import { CustodySection } from "@/features/marketing/components/CustodySection";
import { ScanSection } from "@/features/marketing/components/ScanSection";
import { CodSection } from "@/features/marketing/components/CodSection";
import { ExceptionsSection } from "@/features/marketing/components/ExceptionsSection";
import { RolesSection } from "@/features/marketing/components/RolesSection";
import { ScreenShowcase } from "@/features/marketing/components/ScreenShowcase";
import { FinalCta } from "@/features/marketing/components/FinalCta";
import { MarketingFooter } from "@/features/marketing/components/MarketingFooter";

export default async function RootPage() {
  const isAuthenticated = hasSession();

  return (
    <div className="min-h-screen bg-marketing-bg selection:bg-marketing-accent/40 selection:text-marketing-fg">
      <div aria-hidden="true" className="marketing-grain-overlay" data-testid="marketing-grain-overlay" />
      <div className="relative z-10">
        <MarketingNav isAuthenticated={isAuthenticated} />
        <main>
          <Hero isAuthenticated={isAuthenticated} />
          <PromiseStrip />
          <ThreadLayout>
            <CustodySection />
            <ScanSection />
            <CodSection />
            <ExceptionsSection />
            <RolesSection />
            <ScreenShowcase />
          </ThreadLayout>
          <FinalCta isAuthenticated={isAuthenticated} />
        </main>
        <MarketingFooter />
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Remove superseded files** — run `grep -rn "HowItWorks\|FeatureGrid\|ProductWalkthrough\|Credibility\b" src --include=*.ts --include=*.tsx -l`; delete those four components and their tests once only their own files reference them. Then `grep -rn "RouteLineGraphic\|LifecycleRail" src -l`; delete `RouteLineGraphic` (+ test) if unreferenced; keep `LifecycleRail` (used by `/track/[awb]`). Remove `HOW_IT_WORKS_STEPS`, `HowItWorksStep`, `FEATURES`, `MarketingFeature` from `content.ts` if now unused (and their assertions from `content.test.ts`; also drop unused lucide imports).

- [ ] **Step 7: Run tests**

Run: `npx vitest run src/features/marketing src/app/page.test.tsx`
Expected: PASS after ScreenShowcase conversion (Task 5). If `page.test.tsx` fails only because `ScreenShowcase` still has its own `h2` inside a non-`SectionShell` — proceed to Task 5 and re-run there. Everything else must pass now.

- [ ] **Step 8: Commit**

```bash
git add -A src/web/src
git commit -m "feat(web): custody-thread sections 01-05 and recomposed landing page

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Screens section, Nav, Footer, Final CTA

**Files:**
- Modify: `components/ScreenShowcase.tsx` (+test), `MarketingNav.tsx`, `MarketingFooter.tsx`, `FinalCta.tsx` (+ their tests only where assertions encode old markup)

**Interfaces:**
- Consumes: `SectionShell`, `MonoLabel`, `AppScreenshotFrame`, `SCREENSHOTS`, `FOOTER_LINKS`, `getPrimaryCta`.
- Produces: `<ScreenShowcase />` (section 06, `id="screens"`), unchanged signatures for Nav/Footer/FinalCta.

- [ ] **Step 1: Update `ScreenShowcase.test.tsx`** — keep existing assertions about four screenshots (alt text, captions); add:

```tsx
it("is checkpoint section 06 with a labelled h2", () => {
  const { container } = render(<ScreenShowcase />);
  expect(container.querySelector("section#screens")).toHaveAttribute("aria-labelledby", "screens-heading");
  expect(screen.getByText("06 — Screens")).toBeInTheDocument();
});
```

- [ ] **Step 2: Run to verify the new assertion fails** — `npx vitest run src/features/marketing/components/ScreenShowcase.test.tsx`.

- [ ] **Step 3: Rewrite `ScreenShowcase.tsx`**

```tsx
import { SCREENSHOTS } from "../content";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";

export function ScreenShowcase() {
  return (
    <SectionShell
      id="screens"
      index={6}
      eyebrow="Screens"
      title="The actual product, not a mockup."
      lede="Every screen below is the real app, running against fictional seed data."
    >
      <div className="grid grid-cols-1 gap-x-8 gap-y-12 lg:grid-cols-2">
        {SCREENSHOTS.map((screenshot, index) => (
          <figure key={screenshot.id} className={`flex flex-col gap-3 ${index % 2 === 1 ? "lg:mt-16" : ""}`}>
            <AppScreenshotFrame src={screenshot.src} alt={screenshot.alt} hoverTilt />
            <figcaption className="flex flex-col gap-1">
              <MonoLabel className="text-marketing-accent">Fig. {index + 1}</MonoLabel>
              <span className="text-sm text-marketing-muted-fg">{screenshot.caption}</span>
            </figcaption>
          </figure>
        ))}
      </div>
    </SectionShell>
  );
}
```

- [ ] **Step 4: Edit `AppScreenshotFrame.tsx`** — flatten to the new shape language: change `rounded-xl` → `rounded-md` and the shadow `shadow-[0_0_60px_-15px_hsl(var(--marketing-accent)/0.5)]` → `shadow-[8px_8px_0_0_hsl(var(--marketing-accent)/0.18)]`; change the three chrome dots from `rounded-full` to `rounded-sm`. Run `npx vitest run src/features/marketing/components/AppScreenshotFrame.test.tsx` and update only class-string assertions if any.

- [ ] **Step 5: Restyle `MarketingNav.tsx`** — apply exactly these edits: header class `bg-marketing-bg/90 backdrop-blur` → `bg-marketing-bg/95` (no blur); logo tile `rounded-lg` → `rounded-md`; CTA link `rounded-lg` → `rounded-md`. Nothing else (links/labels unchanged).

- [ ] **Step 6: Rewrite `MarketingFooter.tsx`**

```tsx
import Link from "next/link";
import { FOOTER_LINKS } from "../content";
import { MonoLabel } from "./MonoLabel";

export function MarketingFooter() {
  return (
    <footer className="border-t border-marketing-border/60 px-4 py-12">
      <div className="mx-auto flex max-w-6xl flex-col gap-8 md:flex-row md:items-end md:justify-between">
        <div className="flex flex-col gap-2">
          <span className="font-display text-lg font-semibold text-marketing-fg">Courier Platform</span>
          <MonoLabel>Nepal domestic courier operations</MonoLabel>
        </div>
        <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
          {FOOTER_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="rounded-sm text-sm text-marketing-muted-fg transition-colors hover:text-marketing-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
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

- [ ] **Step 7: Rewrite `FinalCta.tsx`** (standalone — also used on `/pricing`, which has no thread)

```tsx
import Link from "next/link";
import { getPrimaryCta } from "../content";
import { MarketingGrid } from "./MarketingGrid";
import { MonoLabel } from "./MonoLabel";

export function FinalCta({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);

  return (
    <section className="relative overflow-hidden border-t border-marketing-border/60">
      <MarketingGrid />
      <div className="mx-auto flex max-w-6xl flex-col gap-8 px-4 py-24 md:py-32">
        <MonoLabel className="text-marketing-accent">Status · Ready to scan</MonoLabel>
        <h2 className="max-w-4xl font-display text-4xl font-semibold leading-[1.02] tracking-tight text-marketing-fg sm:text-5xl md:text-7xl">
          Run your courier operation on one system.
        </h2>
        <p className="max-w-xl text-lg text-marketing-muted-fg">
          Branches, riders, merchants, and COD — all traceable, from booking to settlement.
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <Link
            href={cta.href}
            className="rounded-md bg-marketing-accent px-6 py-3 text-sm font-medium text-marketing-accent-fg shadow-[4px_4px_0_0_hsl(var(--marketing-fg)/0.18)] transition-transform hover:-translate-y-0.5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
          >
            {cta.label}
          </Link>
          <Link
            href="/track"
            className="rounded-md border border-marketing-border px-6 py-3 text-sm font-medium text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
          >
            Track a parcel
          </Link>
        </div>
      </div>
    </section>
  );
}
```

Add to `FinalCta.test.tsx`:

```tsx
it("offers a secondary Track a parcel link to /track", () => {
  render(<FinalCta isAuthenticated={false} />);
  expect(screen.getByRole("link", { name: "Track a parcel" })).toHaveAttribute("href", "/track");
});
```

- [ ] **Step 8: Run tests**

Run: `npx vitest run src/features/marketing src/app/page.test.tsx src/app/pricing`
Expected: PASS. Fix only assertions that encode removed markup; behavioral assertions (CTA hrefs, one h1, heading order, landmarks) must keep passing unchanged. Note: `page.test.tsx` "getByRole link Track a parcel" might now find several — switch such queries to `getAllByRole` if needed.

- [ ] **Step 9: Commit**

```bash
git add -A src/web/src
git commit -m "feat(web): screens section 06, flatter nav/footer, editorial final CTA

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Pricing page restyle

**Files:**
- Modify: `components/PricingPlans.tsx`, `components/PricingFaq.tsx`; tests only where they encode removed class-based markup (existing behavioral tests must stay green)

**Interfaces:** Consumes `MonoLabel`, `MarketingGrid`. Signatures unchanged.

- [ ] **Step 1: Add a failing test** to `PricingPlans.test.tsx`:

```tsx
it("labels the section with a mono eyebrow and keeps plan cards square-cornered (no rounded-xl)", () => {
  const { container } = render(<PricingPlans isAuthenticated={false} />);
  expect(screen.getByText("Plans")).toBeInTheDocument();
  expect(container.querySelector(".rounded-xl")).toBeNull();
});
```

- [ ] **Step 2: Run to verify failure** — `npx vitest run src/features/marketing/components/PricingPlans.test.tsx`.

- [ ] **Step 3: Edit `PricingPlans.tsx`**
  - Import `MonoLabel` and `MarketingGrid`.
  - In `PlanCard`: replace both `rounded-xl` with `rounded-md`. Default plan class: swap `shadow-[0_0_60px_-14px_hsl(var(--marketing-accent)/0.65)] lg:-translate-y-3 lg:scale-[1.03]` for `shadow-[8px_8px_0_0_hsl(var(--marketing-accent)/0.25)] lg:-translate-y-3`. Non-default hover: replace `hover:shadow-[0_0_40px_-15px_hsl(var(--marketing-accent)/0.4)]` with `hover:shadow-[6px_6px_0_0_hsl(var(--marketing-accent)/0.18)] motion-safe:transition-transform motion-safe:hover:-translate-y-0.5`. Badge: `rounded-full` → `rounded-sm`. Both CTA `rounded-lg` → `rounded-md`. Price `text-3xl` → `text-4xl`.
  - In `PricingPlans`: add `<MarketingGrid />` as first child of a `relative` section (`className="relative mx-auto max-w-6xl px-4 pb-24 pt-20"` → wrap: `<section id="plans" className="relative overflow-hidden"><MarketingGrid /><div className="relative mx-auto max-w-6xl px-4 pb-24 pt-20 md:pt-28"> … </div></section>`); heading block becomes left-aligned: `items-start text-left`; insert `<MonoLabel className="text-marketing-accent">Plans</MonoLabel>` above the `h1`; `h1` size → `font-display text-4xl font-semibold leading-[1.05] tracking-tight text-marketing-fg sm:text-5xl md:text-6xl`; trial pill `rounded-full` → `rounded-sm`.

- [ ] **Step 4: Edit `PricingFaq.tsx`** — heading block left-aligned (`items-start text-left`), add `<MonoLabel className="text-marketing-accent">FAQ</MonoLabel>` above the `h2`, `max-w-3xl` → `max-w-6xl` wrapper with inner `max-w-3xl`; keep a single `h2` and the `h3` questions (existing test requires exactly one h2).

- [ ] **Step 5: Run tests** — `npx vitest run src/features/marketing/components/PricingPlans.test.tsx src/features/marketing/components/PricingFaq.test.tsx src/app/pricing`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src/web/src
git commit -m "feat(web): restyle pricing page to the custody-thread visual language

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: /track and auth showcase panel

**Files:**
- Modify: `src/web/src/app/track/page.tsx`, `src/web/src/app/track/[awb]/page.tsx`, `src/web/src/features/tracking/components/TrackingTimeline.tsx`, `src/web/src/components/auth/AuthShowcasePanel.tsx` and their existing tests where they encode changed markup

**Interfaces:** Consumes `MarketingGrid`, `MonoLabel`, `ScanBar` (marketing tokens — allowed on `/track`, never in auth). Auth uses only `--auth-panel-*` tokens.

- [ ] **Step 1: Failing tests.**

Add to `src/app/track/page.test.tsx`:

```tsx
it("renders the public-tracking eyebrow and the decorative grid", () => {
  const { container } = render(<TrackPage />);
  expect(screen.getByText(/public tracking/i)).toBeInTheDocument();
  expect(container.querySelector('[data-testid="marketing-grid"]')).not.toBeNull();
});
```

(Use the file's existing render/wrapper setup — copy how its other tests render `TrackPage`.) Add to the AuthShowcasePanel test (create `src/components/auth/AuthShowcasePanel.test.tsx` if none exists):

```tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { AuthShowcasePanel } from "./AuthShowcasePanel";

describe("AuthShowcasePanel", () => {
  it("shows a mono sample-AWB stamp above the headline", () => {
    render(
      <AuthShowcasePanel
        icon={Package}
        brand="Courier Platform"
        headline="Headline"
        description="Desc"
        stops={[{ label: "A", caption: "a", tone: "neutral" }]}
      />
    );
    expect(screen.getByText(/AWB NP2026-000000 · Sample/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify failure** — `npx vitest run src/app/track src/components/auth`.

- [ ] **Step 3: `track/page.tsx`** — import `MarketingGrid` and `MonoLabel`. Replace the fixed radial-gradient `div` with `<MarketingGrid className="fixed" />` followed by the existing single radial wash (keep the wash, reduce to `hsl(var(--marketing-accent) / 0.12)`). Icon tile: `rounded-xl` → `rounded-md` and drop its `shadow-[0_0_40px_-8px…]` in favour of `shadow-[4px_4px_0_0_hsl(var(--marketing-fg)/0.18)]`. Above the `h1` insert `<MonoLabel className="text-marketing-accent">Public tracking · No account needed</MonoLabel>`. `h1` size → `text-[clamp(2.75rem,8vw,5rem)] leading-[0.98]`.

- [ ] **Step 4: `track/[awb]/page.tsx`** — read the file, then: replace every `rounded-xl` with `rounded-lg`; replace any accent glow `shadow-[0_0_…]` with `shadow-[6px_6px_0_0_hsl(var(--marketing-accent)/0.18)]` on the primary status card only and remove it elsewhere; wrap the AWB display in `font-mono tracking-[0.14em]`. Do not change data hooks, error/empty/loading states, or copy.

- [ ] **Step 5: `TrackingTimeline.tsx`** — read the file, then restyle the event rail to the thread language: 1px `bg-marketing-border` vertical rail, `rounded-sm` square nodes (`h-3 w-3`), the most recent event's node filled `bg-marketing-accent`, timestamps in `font-mono text-xs`. Keep every existing prop, ordering and text unchanged so `TrackingTimeline.test.tsx` passes unmodified.

- [ ] **Step 6: `AuthShowcasePanel.tsx`** — before the `<h2>` add:

```tsx
<p className="mb-4 font-mono text-[0.6875rem] uppercase tracking-[0.14em] text-auth-panel-foreground/70">
  AWB NP2026-000000 · Sample
</p>
```

and change the h2's `xl:text-4xl` to `xl:text-5xl`. No other change (fixed auth tokens only).

- [ ] **Step 7: Run tests** — `npx vitest run src/app/track src/features/tracking src/components/auth src/app/login src/app/register`
Expected: PASS; auth page tests (one h1 per page) still pass.

- [ ] **Step 8: Commit**

```bash
git add -A src/web/src
git commit -m "feat(web): apply custody-thread language to /track and the auth showcase panel

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: Documentation

**Files:**
- Modify: `DESIGN.md`, `PRODUCT.md`, `docs/FRONTEND-ROADMAP.md`, `docs/superpowers/specs/2026-09-21-marketing-visual-redesign-design.md`

- [ ] **Step 1: Amend the spec** so it matches what was built. Edit these spots:
  - §3 section order and §5 components: checkpoints run 01–06 (Custody, Scan & manifest, COD, Exceptions, Roles, Screens); `PromiseStrip` sits between Hero and the thread; `FinalCta` is standalone (also used by `/pricing`).
  - §5 `Thread`: implemented as `ThreadLayout` — a scroll-scaled vertical rail (no measured path); add `Reveal`, `CountUp`, `StageList`, `MarketingGrid`.
  - §4/§5 auth: auth surfaces get only a mono sample-AWB stamp and a larger headline (`--auth-panel-*` tokens; marketing `MockCard` is not used there because of the Two-Room Rule).
  - §6: no `next/dynamic` — motion is confined to tiny client leaves; sections are otherwise server-rendered.
  - §11: replace the "measured checkpoints" risk mitigation with "rail is scroll-scaled, so it is independent of section height".

- [ ] **Step 2: `DESIGN.md`** — in "Marketing System ("Night Dispatch")", append after the Two-Room Rule paragraph:

```markdown
**Custody Thread (F14).** The marketing pages share one structural motif: a single vertical rail (`ThreadLayout`) runs beside the sections and each section is a numbered `Checkpoint` (Plex Mono numerals, `01`–`06`) on it — the page reads like a parcel's scan history. The rail's accent overlay is scroll-linked (`scaleY`), fully drawn under `prefers-reduced-motion`, and `aria-hidden`. Tokens are unchanged; the deltas are shape and type:

- **Shape:** radii are `rounded-sm`/`rounded-md`/`rounded-lg` (4–8px), never `rounded-xl` or pills on marketing cards. Depth comes from hairline `--marketing-border` borders and a hard offset shadow (`shadow-[6px_6px_0_0_hsl(var(--marketing-accent)/0.18)]`), not blur or glow. No `backdrop-blur` on marketing surfaces.
- **Type:** Space Grotesk display headings scale up to `clamp(2.75rem, 8vw, 6.5rem)` on the hero with tight tracking; IBM Plex Mono (`font-mono`) is reserved for AWBs, checkpoint numerals, stamps, and eyebrow labels (`MonoLabel`), always uppercase with `tracking-[0.14em]`.
- **Diagrams:** explanatory graphics (custody chain, COD waterfall, manifest reconcile) are inline SVG generated from data and pure helpers in `features/marketing/lib/diagramMath.ts`; they are `aria-hidden` with an sr-only text equivalent inside a labelled `<figure>`, and use only `--marketing-*` and `--marketing-tone-*` colors. Color is never the only carrier of meaning (missing manifest cells are X-marked; nodes differ by fill).
- **Mock cards:** `MockCard` shows fictional sample data and always carries a visible "Sample data" tag. Never fabricate metrics, customers, or testimonials.
- **Motion:** transform/opacity only — thread scale, one-time `CountUp`, `Reveal`, and the CSS `.marketing-scan-line` sweep. Every one has a fully visible static fallback under `prefers-reduced-motion`.
- **Shared surfaces:** `/`, `/pricing`, `/track`, and `/track/[awb]` use these primitives; the auth showcase panel receives only a mono sample-AWB stamp and the larger headline, staying on its fixed `--auth-panel-*` tokens.
```

Also update the "Don't" list only if a line contradicts (e.g., mentions glow) — leave otherwise.

- [ ] **Step 3: `PRODUCT.md`** — in the "Prospective courier company (marketing visitor)" persona bullet, append: "Marketing, `/pricing`, and public `/track` share one visual language, the Custody Thread (`docs/FRONTEND-ROADMAP.md` Phase F14)."

- [ ] **Step 4: `docs/FRONTEND-ROADMAP.md`** — before the line "All frontend phases planned through F13 are complete." add (and update that sentence to "through F14"):

```markdown
## Phase F14 — Marketing Visual Redesign ("Custody Thread")

**Status:** complete (2026-09-21).
**Spec:** `docs/superpowers/specs/2026-09-21-marketing-visual-redesign-design.md` · **Plan:** `docs/superpowers/plans/2026-09-21-marketing-visual-redesign.md`

Second visual pass over the public surfaces, evolving (not replacing) Night Dispatch:

- Editorial hero (fluid oversized headline, mono AWB metadata row, real dashboard screenshot overlapped by three labelled sample-data cards).
- Scroll-linked custody thread with numbered checkpoints 01–06: custody chain, scan & manifest, COD ledger, exceptions (NDR/RTO), roles & branch scope, screens.
- Three inline-SVG diagrams driven by data and pure helpers (`codBreakdown`, `reconcileManifest`).
- `/pricing`, `/track`, `/track/[awb]` restyled with shared primitives; auth showcase panel gets a mono stamp.
- No new dependencies; every animation has a reduced-motion static fallback; WCAG AA preserved.
- Exit criteria: `npm test`, `npm run lint`, `npm run build` green; visual check of `/`, `/pricing`, `/track`, `/login`, `/register` in both themes at 375px and 1440px.
```

- [ ] **Step 5: Commit**

```bash
git add DESIGN.md PRODUCT.md docs
git commit -m "docs: document F14 marketing visual redesign (design system, roadmap, spec amendments)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 9: Verification and visual QA

**Files:** none new (fixes only).

- [ ] **Step 1: Full test run** — `npm test` (from `src/web`). Expected: all green. Fix regressions caused by this work only.
- [ ] **Step 2: Lint** — `npm run lint`. Expected: no errors (remove any unused imports left from deleted sections).
- [ ] **Step 3: Type + build** — `npm run build`. Expected: success, no type errors.
- [ ] **Step 4: Visual QA** — start the dev server (`npm run dev` in `src/web`), then with browser automation load `/`, `/pricing`, `/track`, `/login`, `/register` at 375px and 1440px in both light and dark (toggle via the marketing theme toggle). Check: no horizontal scroll at 375px; thread + checkpoints aligned with each section heading; diagrams legible (custody chain vertical below `lg`, horizontal at `lg`+); hero cards overlap cleanly on `md`+ and stack on mobile; hero screenshot is the LCP element; nothing overflows; focus rings visible on keyboard tab.
- [ ] **Step 5: Reduced motion** — emulate `prefers-reduced-motion: reduce`; confirm the thread is fully drawn, scan bar static, counts show final values, sections visible without scrolling triggers.
- [ ] **Step 6: Contrast spot-check** — verify `text-marketing-accent` labels and `text-marketing-muted-fg` small mono labels against `bg-marketing-bg` and `bg-marketing-bg-elevated` in both themes (≥4.5:1); if any fails, switch that label to `text-marketing-fg`. Record results in `globals.css`'s F13 header comment only if a token/opacity was newly introduced (none expected).
- [ ] **Step 7: Two-Room check** — `grep -rn "marketing-" src/app/(staff) src/app/merchant src/app/platform src/app/rider src/components/auth` returns nothing.
- [ ] **Step 8: Commit any fixes**

```bash
git add -A src/web
git commit -m "fix(web): address F14 verification findings

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

Report: state exactly what passed (test count, lint, build) and any visual issue left, without hedging.
