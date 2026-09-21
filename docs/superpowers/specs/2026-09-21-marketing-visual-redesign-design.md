# Marketing Visual Redesign ("Custody Thread") — Design Spec

## 1. Context

The public marketing surface (`/`, `/pricing`) was built in Frontend Phase F13 and
redesigned once on 2026-09-07 (real app screenshots, `AppScreenshotFrame`,
`RouteLineGraphic`, Framer Motion reveals). It is competent but reads as a
standard dark SaaS template: a two-column hero, a uniform feature grid, flat
section rhythm, and no diagrams that explain the product's actual mechanism.

This spec is a second visual pass. It **evolves** the Night Dispatch identity
(confirmed with the user) rather than replacing the palette, and extends to all
public-facing surfaces: `/`, `/pricing`, `/track`, and the auth pages
(`/login`, `/register`, `/accept-invite`, `/session-expired` surfaces).

Not in scope: backend, routing, IA changes beyond section order on `/`, new
dependencies, any invented brand name/logo/testimonial/metric.

## 2. Concept: the Custody Thread

The product's core promise is a traceable chain of custody. The page itself
expresses that: a single continuous vertical **thread** (a scroll-scaled rail) runs down the page and each
section is a numbered **checkpoint** on it, like a parcel's scan history.
Diagrams and UI mockups attach to checkpoints. The thread is functional
(orientation + progress + section transition), not decoration.

## 3. Goals / Exit Criteria

- Editorial hero: fluid oversized headline, mono metadata row
  (`AWB · KTM → PKR · IN TRANSIT`, clearly fictional sample), the real dashboard
  screenshot offset and overlapped by CSS-built cards (scan confirmation, COD
  ledger strip, manifest count).
- Three inline-SVG explanatory diagrams: custody chain, COD waterfall
  (2,500 collected − 150 fee = 2,350 merchant payable, per CLAUDE.md §22),
  manifest reconcile with one flagged missing shipment.
- Section order on `/`: Hero → Promise strip → checkpoints 01–06 (Custody chain, Scan &
  manifest, COD ledger, NDR/RTO exceptions, Roles & branch scope, Screens) → Final CTA.
- Shared primitives: `ThreadLayout`, `Checkpoint`, `SectionShell`, `MonoLabel`,
  `MockCard` in `features/marketing/components/`. `/pricing` and `/track` consume the
  shape/type language and `MonoLabel`/`MarketingGrid`.
- The thread draws with scroll (`useScroll`); scan-line sweep on mockups; one-time
  count-up on numbers; hover micro-interactions on cards/links.
- Every animation has a `prefers-reduced-motion` fallback that renders the
  complete static state.
- `npm run build`, `npm run lint`, `npm test` green; no regressions elsewhere.

## 4. Visual System (deltas to DESIGN.md "Marketing System")

- **Color:** existing `--marketing-*` tokens unchanged. Indigo remains the one
  signal accent; the existing amber `--marketing-tone-motion` is used only for
  in-transit state. No purple/pink gradients. Gradients are single-hue,
  low-opacity, and used only to layer depth behind mockups.
- **Type:** Space Grotesk display scale up to `clamp(3rem, 8vw, 7rem)` with tight
  tracking on the hero; IBM Plex Mono (already loaded via `--font-plex-mono`)
  for AWB numbers, checkpoint numerals, and stamps only. Body stays Plex Sans.
- **Shape:** radii 4–8px (down from current), hairline `--marketing-border`
  borders, depth from layered flat panels and offset — no glassmorphism, no
  heavy blur, no oversized pill cards.
- **Grain overlay:** retained as-is (`.marketing-grain-overlay`).
- **Two-Room Rule:** `--marketing-*` tokens still never appear in staff /
  merchant / platform / rider surfaces. Auth pages keep their fixed
  `--auth-panel` tokens and receive only a mono sample-AWB stamp and a larger headline.

## 5. Components

New (under `src/web/src/features/marketing/components/`):

| Component | Purpose |
|---|---|
| `ThreadLayout` | Wraps the checkpoint sections with a vertical rail whose accent overlay is scaled by `useScroll`/`useTransform` (no measured path); fully drawn when reduced-motion. `aria-hidden`. |
| `Checkpoint` | Numbered node (`01`–`06`, Plex Mono) anchored to the thread at a section; lights up when in view. |
| `SectionShell` | Section wrapper: checkpoint, eyebrow, heading, background tone shift for transitions. Owns heading-level structure. |
| `MonoLabel` | Small uppercase mono label/stamp. |
| `Reveal`, `CountUp`, `StageList`, `MarketingGrid`, `PromiseStrip` | Scroll reveal; one-time count-up; vertical lifecycle rail; faint waybill grid background; three-claim strip between Hero and the thread. |
| `MockCard` | CSS-built UI card (scan confirmation, COD strip, manifest count); fictional data, labelled "Sample data". |
| `CustodyChainDiagram`, `CodWaterfallDiagram`, `ManifestReconcileDiagram` | Inline SVG diagrams inside a labelled `<figure>`; SVGs are `aria-hidden` and an sr-only text equivalent carries the content. |

Reworked: `Hero`, `ScreenShowcase`, `FinalCta`, `MarketingNav`, `MarketingFooter`,
`PricingPlans`, `PricingFaq`, `AppScreenshotFrame`. Replaced by the five section components
(`CustodySection`, `ScanSection`, `CodSection`, `ExceptionsSection`, `RolesSection`) and removed:
`HowItWorks`, `FeatureGrid`, `ProductWalkthrough`, `Credibility`, `RouteLineGraphic`.
`LifecycleRail` is retained (used by `/track/[awb]`).

Other surfaces: `/track` and `/track/[awb]` (uses `TrackingTimeline`) adopt the shape/type
language and square timeline nodes; `FinalCta` is standalone (also used by `/pricing`). Auth
showcase panel gets only a mono sample-AWB stamp and a larger headline — `MockCard` uses
`--marketing-*` tokens, which the Two-Room Rule keeps out of auth surfaces.

## 6. Motion & Performance

- Framer Motion only (already a dependency). No new libraries.
- Motion is confined to tiny client leaves (`ThreadLayout`, `Checkpoint`, `Reveal`,
  `CountUp`); diagrams and sections are otherwise server-renderable. No `next/dynamic`
  split was needed.
- LCP element is the hero screenshot (`priority`, existing webp). No layout
  shift: mockups reserve dimensions; fonts already `next/font`.
- Animate `transform`/`opacity`/`stroke-dashoffset` only.
- `useReducedMotion` gates every animation; static state fully visible.

## 7. Accessibility

- All new text/graphics verified WCAG AA in light and dark (4.5:1 text, 3:1
  non-text); ratios recorded in the `globals.css` marketing header comment if
  any new token/opacity is introduced.
- Decorative thread/glows `aria-hidden`; diagrams have text alternatives.
- Landmark + single-`h1`/ordered-heading structure preserved; focus-visible
  rings on all interactive elements; keyboard-reachable nav and CTAs.
- Responsive: mobile-first, thread collapses to a left-edge rail below `md`; no
  horizontal scroll at 320px.

## 8. Content Rules

No fabricated brand, logos, testimonials, customer counts, or performance
claims. Mockup data (AWBs, names, amounts) is fictional and labelled. CTAs
unchanged: `getPrimaryCta` (`/register` or `/dashboard`), secondary anchors and
`/login` links preserved.

## 9. Testing

- Update existing component tests for restructured sections.
- New tests: each new primitive; reduced-motion renders static state; heading
  order and landmarks on `/`; CTA auth-awareness unchanged; diagrams expose
  accessible names; nav/anchor targets exist.
- Gate: `npm run build`, `npm run lint`, `npm test` all pass. Visual check of
  `/`, `/pricing`, `/track`, `/login`, `/register` in both themes at 375px and
  1440px via browser automation before completion.

## 10. Documentation Updates

- `DESIGN.md`: update "Marketing System" (thread, mono usage, radius, motion and
  diagram rules); keep Two-Room Rule text.
- `docs/FRONTEND-ROADMAP.md`: add Phase F14 "Marketing Visual Redesign".
- `PRODUCT.md`: only if the marketing scope note changes (auth/track now share
  the motif).
- This spec and the implementation plan under `docs/superpowers/`.

## 11. Risks

- Scroll-linked thread across variable section heights → the rail is scroll-scaled, so it
  is independent of section height; verify at multiple widths.
- Scope creep into auth surfaces → limit to the mono stamp and headline scale; no changes
  to auth logic or `--auth-panel` tokens.
- Diagram legibility on mobile → design diagrams with a vertical layout variant
  below `md`.
