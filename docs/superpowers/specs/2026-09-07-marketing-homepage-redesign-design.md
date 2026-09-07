# Marketing Homepage Redesign — Design Spec

## 1. Context

Frontend Phase F13 (`docs/superpowers/specs/2026-09-07-frontend-phase-f13-marketing-landing-page-design.md`)
shipped the public marketing page at `/` — the "Night Dispatch" dark identity,
Hero/FeatureGrid/HowItWorks/ProductWalkthrough/Credibility/FinalCta/Footer
sections, Framer Motion animation, and a stylized `ProductMockupPanel` used
as both the hero visual and the walkthrough centerpiece.

The user has now seen the shipped page and finds it too plain/generic, the
stylized mockup unconvincing, the visual hierarchy flat, and the motion too
subtle. This spec covers a visual redesign of the same route — **not** a
new page, not a new information architecture, not a backend or routing
change. It builds on F13's structure and confirmed constraints (no
fabricated identity/testimonials, "Courier Platform" working name, CTAs to
`/login`, `prefers-reduced-motion` support, WCAG AA) rather than replacing
them.

Confirmed with the user before design:

- Keep the dark "Night Dispatch" identity as the foundation; make it bolder
  and more graphic rather than switching to a different palette/register.
- Replace the stylized dashboard mockup with **real screenshots of the
  running app**, captured live against the actual local dev stack with
  seeded (fictional) data — not hand-drawn recreations.
- Four screens to capture: org dashboard, shipment list/detail, scan/
  manifest screen, COD/settlement screen.
- Add graphic/depth elements (glow, gradient, a route-line motif) and more
  pronounced motion (parallax, hover tilt/glow) than F13 shipped with.

## 2. Goals / Exit Criteria

- The four screens above are captured as real PNG screenshots from the
  actual running app (seeded test organization, fictional data only — no
  real customer data, per `PRODUCT.md`'s no-fabricated-evidence
  constraint) and committed under `src/web/public/marketing/screenshots/`.
- Hero's visual is the real dashboard screenshot (framed), not
  `ProductMockupPanel`.
- A new `ScreenShowcase` section presents all four screenshots as the
  page's dedicated proof section.
- `ProductWalkthrough`'s centerpiece is the real shipment-detail screenshot,
  with the existing animated lifecycle rail retained alongside it.
- `FeatureGrid` tiles get gradient-border/hover-glow treatment.
- A reusable `AppScreenshotFrame` component (browser-chrome-style frame,
  border glow, drop shadow, optional hover tilt) wraps every screenshot use.
- A subtle route-line background graphic appears behind the Hero and
  HowItWorks sections, respecting `prefers-reduced-motion`.
- All F13 exit criteria continue to hold: no fabricated social proof, no
  invented brand name, `prefers-reduced-motion` fallback with fully-visible
  static state, WCAG AA contrast, full landmark/heading structure, auth-aware
  CTA logic unchanged, `npm run build` / `npm run lint` / `npm test` green,
  no regressions to any other route.
- `ProductMockupPanel` and `LifecycleRail` are either reused (rail) or
  removed if fully superseded (panel) — no dead code left behind.

## 3. Screenshot Capture Pipeline

This is infrastructure work that has to happen before any component can
reference a real image.

1. **Local stack:** `docker compose up -d` (project root) brings up
   Postgres (`localhost:5434`), Redis (`6380`), RabbitMQ, Mailhog per the
   existing `docker-compose.yml` — reusing the pattern already established
   for this repo, not introducing new services.
2. **Backend + frontend running locally** (not the Docker `api`/`web`
   images, for faster iteration): `dotnet run` against the API pointed at
   the Postgres container, `npm run dev` for `src/web`.
3. **One throwaway test organization**, created through the real
   registration flow (no direct DB inserts for the account itself) — a
   fictional courier company name, e.g. "Sagarmatha Express" (placeholder-
   style, consistent with F13's "no invented durable brand" rule — this
   name exists only inside seed data for screenshots, never surfaced as
   the product's own brand).
4. **Minimal realistic data**, created through the UI (a few branches
   across 2-3 districts, a handful of shipments spanning different
   statuses — created, in transit, out for delivery, delivered, one NDR —
   so the dashboard/list don't look like an empty state) — enough to make
   four screens look like a real day of operations, not exhaustive.
5. **Capture via Chrome browser automation:** navigate to each of the four
   screens while authenticated, screenshot each at a fixed viewport
   (1440×900, standard desktop marketing-screenshot size), save as PNG.
6. **Post-process:** crop chrome/scrollbars as needed, export both a
   `.png` (source) and a `.webp` (used on the page, per this project's
   existing `img{max-width:100%}` and general image-weight discipline) into
   `src/web/public/marketing/screenshots/`. Target ≤200KB per image.
7. **Teardown:** the test organization stays in the shared dev Postgres
   (harmless seed data, consistent with how this container is already
   reused across worktrees per prior sessions) — not deleted, not
   documented as a "real" tenant anywhere.

If any individual screen proves unreachable within reasonable effort (e.g.
a scan-flow prerequisite that's awkward to seed through the UI), that
screen is flagged to the user rather than silently faked with a redrawn
mockup — real-or-flagged, never a silent substitution.

## 4. Visual System Additions

Scoped entirely to the marketing route's existing token mechanism (the
`--marketing-*` custom properties set by the marketing theme wrapper,
`src/web/src/app/globals.css` search: `--marketing-`) — no changes to the
app's own `:root`/`.dark` tokens.

- **New tokens:** `--marketing-glow` (accent-hued radial glow color),
  `--marketing-grain` (a data-URI noise texture, applied at very low
  opacity via a full-bleed `::before` on the page wrapper).
- **`AppScreenshotFrame` component** (`features/marketing/components/`):
  wraps an `<img>` in a browser-chrome header bar (three dots, matching
  `ProductMockupPanel`'s existing dot/AWB-label styling for continuity),
  border + drop-shadow + accent-tinted glow, and an optional
  `hoverTilt` prop (subtle 3D tilt via Framer Motion's `useMotionValue`/
  mouse-position tracking, gated behind `useReducedMotion` like every
  other animated piece on this page).
- **Route-line graphic:** an inline SVG (`RouteLineGraphic.tsx`) — a thin
  dashed path with 2-3 small circular "hub" nodes, subtly animated
  (dash-offset drift) behind the Hero and HowItWorks sections at low
  opacity. Purely decorative but domain-appropriate (evokes a delivery
  route between hubs, not generic decoration) — respects
  `prefers-reduced-motion` by rendering static.
- **Gradient orbs:** 1-2 large blurred radial-gradient `<div>`s (CSS only,
  no new asset) positioned behind screenshot frames for depth.

## 5. Section-by-Section Changes

### `Hero` (modified)
Layout changes from centered-stack to asymmetric two-column at `lg`:
headline/subhead/CTAs left, `AppScreenshotFrame` wrapping the real
dashboard screenshot right, with a subtle floating (translateY loop)
animation and the route-line graphic drifting behind at low opacity.
Headline scale pushed from `text-6xl` to `text-7xl` at desktop (already
allowed by F13's spec ceiling).

### `ScreenShowcase` (new)
Inserted after `FeatureGrid`, before `HowItWorks`. Four `AppScreenshotFrame`
cards in a responsive grid (2×2 desktop, stacked mobile), each with a short
caption naming the real capability shown (e.g. "Dashboard — today's
shipments, COD, and branch performance at a glance"). Staggered scroll
reveal, consistent with the rest of the page's reveal pattern
(`useRevealVariants`).

### `ProductWalkthrough` (modified)
`ProductMockupPanel` swapped for `AppScreenshotFrame` wrapping the real
shipment-detail screenshot; the existing `LifecycleRail` animated status
strip is retained beneath/beside it (it already does its job — animated,
accessible, reduced-motion-safe — no reason to discard it).

### `FeatureGrid` (modified)
Icon tiles get a gradient border (`--marketing-accent`-tinted, low opacity)
and a hover glow transition. Content/copy unchanged.

### `HowItWorks` (modified)
Structure unchanged (4 steps); route-line graphic added behind the step
sequence to visually connect them.

### `Credibility`, `FinalCta`, `MarketingFooter`, `MarketingNav` (styling only)
No structural change. CTA button in `FinalCta`/`Hero` gets a stronger
accent glow on hover, consistent with the bolder direction — copy and
links untouched.

### `ProductMockupPanel` (removed) / `LifecycleRail` (retained, reused by `ProductWalkthrough`)
`ProductMockupPanel` is fully superseded by `AppScreenshotFrame` +
real screenshots; deleted along with its now-unused test file, after
confirming no other consumer references it.

## 6. New/Modified Files

**New:**
- `src/web/src/features/marketing/components/AppScreenshotFrame.tsx` (+ test)
- `src/web/src/features/marketing/components/ScreenShowcase.tsx` (+ test)
- `src/web/src/features/marketing/components/RouteLineGraphic.tsx` (+ test)
- `src/web/public/marketing/screenshots/{dashboard,shipment-detail,scan-manifest,cod-settlement}.webp`
  — only the optimized `.webp` files are committed; raw `.png` captures are
  intermediate working files (scratchpad), not checked into the repo.

**Modified:**
- `Hero.tsx`, `ProductWalkthrough.tsx`, `FeatureGrid.tsx`, `HowItWorks.tsx`,
  `FinalCta.tsx` (CTA glow only)
- `content.ts` — add a `SCREENSHOTS` data array (src, alt, caption per
  screen) consumed by `ScreenShowcase` and `Hero`/`ProductWalkthrough`
- `globals.css` — new `--marketing-glow`/`--marketing-grain` tokens
- Corresponding `*.test.tsx` files for every modified component

**Removed:**
- `ProductMockupPanel.tsx` + its test

## 7. Accessibility

- Every screenshot `<img>` gets descriptive, generic `alt` text (e.g. "Org
  dashboard showing today's shipment counts, COD summary, and branch
  performance" — describing the UI, never naming or implying real customer
  data).
- `AppScreenshotFrame`'s hover-tilt motion is gated behind
  `useReducedMotion`, same as every other animated piece; static frame with
  no transform when reduced motion is on.
- `RouteLineGraphic`'s dash animation likewise gated; renders as a static
  path otherwise. It is `aria-hidden` (purely decorative, not
  informational).
- New gradient/glow tokens checked against WCAG AA for any text placed over
  them (headline over the Hero's gradient background, CTA label over the
  glow button) using the dataviz skill's contrast validator, same tool
  F13's color pass used.
- Heading order and landmark structure unchanged by this pass (no new
  `<h2>`s beyond `ScreenShowcase`'s own, which fits the existing
  section-per-`<h2>` pattern) — reverified by the existing accessibility
  test.

## 8. Testing

- New component tests for `AppScreenshotFrame`, `ScreenShowcase`,
  `RouteLineGraphic`: renders, alt text present and non-empty, reduced-
  motion fallback renders the static/final state.
- Updated tests for every modified section (`Hero`, `ProductWalkthrough`,
  `FeatureGrid`, `HowItWorks`) reflecting the new markup while keeping
  existing assertions (copy, CTA hrefs) intact.
- `ProductMockupPanel.test.tsx` removed along with the component.
- Full `npm run build`, `npm run lint`, `npm test` green, no regressions
  to `page.test.tsx` or any other route's tests.

## 9. Non-Goals (explicit deferrals)

- No change to routing, auth-aware CTA logic, or any other route
  (`/login`, `/dashboard`, `/track`, etc.) — this is a visual-only pass on
  the existing `/` page.
- No new backend endpoints, no schema change. The seeded test organization
  used for screenshots is throwaway dev data, not a product feature.
- No brand rename — "Courier Platform" (or a clearly fictional seed-data
  company name, confined to screenshot content) stays the working name.
- No CMS, no image-upload pipeline, no responsive art-direction beyond
  standard `max-width:100%` — screenshots are static assets shipped with
  the build, same as any other marketing image in this stack.
- No SEO/OG-image/analytics work — unchanged from F13's deferral.
