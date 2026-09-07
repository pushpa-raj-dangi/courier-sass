# Frontend Phase F13 — Public Marketing Landing Page — Design Spec

## 1. Context

Frontend Phases F0–F12 built the staff back-office, merchant portal, public
tracking (`/track/{awb}`), and rider mobile-web PWA — all operational
surfaces following the "Ledger Counter" design system (`DESIGN.md`): muted,
paper/ink, built for scan-heavy work under time pressure, explicitly **not**
a storefront (`DESIGN.md` §Product Principles: *"clarity, density, and
correctness outrank marketing polish"*).

This phase adds the one surface that role deliberately excludes: a public
marketing page that sells the platform itself to prospective courier
companies. It is visually and architecturally distinct from every prior
phase — inspired by impeccable.style's approach (a restrained, mostly
monochrome palette that earns its "graphic and animation heavy" reputation
through *interaction* — before/after comparisons, live embedded demos,
animated step walkthroughs — rather than decorative illustration), adapted
to this product's own domain (parcel custody, scan-driven operations, COD
traceability) instead of copying impeccable.style's content.

Confirmed with the user before design (see conversation record):

- **Purpose:** public marketing site for the SaaS itself, not an in-app page
  and not a throwaway portfolio piece — real product surface, subject to
  `PRODUCT.md`/`DESIGN.md` constraints (no fabricated testimonials/logos/
  metrics, no invented brand identity beyond the existing working name).
- **Naming:** uses "Courier Platform" (the existing working name already
  live on the login page) throughout. Swappable later; not blocked on a
  real brand name.
- **Visual identity:** a **distinct** marketing identity ("Night Dispatch"),
  not a bolder version of Ledger Counter — see §3.
- **Hero centerpiece:** an animated **product mockup walkthrough** (stylized
  seeded-data dashboard/scan screens animating through a delivery), not an
  abstract diagram.
- **Social proof:** none fabricated. Replaced with a domain-credibility
  section grounded in real, verifiable specifics (Nepal geography, NPR-only
  COD, real scan/manifest operations) rather than customer logos.
- **Sections:** full-length page — Hero, Feature grid, How it works, Product
  walkthrough, Credibility, Final CTA, Footer.
- **Routing:** `/` becomes the public marketing page for everyone (see §2).
- **CTA target:** all CTAs link to `/login` (existing staff/merchant sign-in
  — appropriate for B2B software onboarded manually, not self-serve signup).
- **Motion:** Framer Motion, added as a new dependency, scoped to this
  route only.

No backend changes. No new API endpoints, no new data. This is a static,
client-rendered marketing page composed from copy and the existing design
token/component conventions (extended, not replaced, for this one route).

## 2. Goals / Exit Criteria

- `/` renders the public marketing page for every visitor — authenticated
  or not. No hard redirect away from `/` for anyone.
- An authenticated visitor sees a "Go to dashboard" affordance in the
  marketing nav instead of "Sign in"; an unauthenticated visitor sees
  "Sign in". Both are server-computed (same `getValidAccessToken()` check
  the old redirect used), never a client-side guess.
- `/login` and `/dashboard` behavior is completely unchanged — no user who
  currently lands correctly today lands anywhere different, except that
  `/` itself no longer immediately redirects them.
- The page reads as visually and tonally distinct from the back-office app
  (dark "Night Dispatch" identity, §3) while sharing its type pairing
  (Space Grotesk / IBM Plex Sans) for brand continuity.
- Scroll-triggered, staggered, and orchestrated animations throughout
  (hero entrance, feature grid reveal, how-it-works steps, product-mockup
  walkthrough sequence), all respecting `prefers-reduced-motion` with a
  fully-visible static fallback — same pattern already established by
  `AuthLifecycleGraphic` (`src/web/src/components/auth/AuthLifecycleGraphic.tsx`).
- Zero fabricated social proof: no invented customer names, logos,
  testimonials, or usage statistics anywhere on the page.
- All copy uses "Courier Platform" as the working name; nothing hardcodes
  a different/invented brand name.
- `npm run build`, `npm run lint`, and `npm test` (Vitest) all pass with the
  new Framer Motion dependency in place.
- No regression to any existing route, test, or the PWA rider scope.

## 3. Visual Identity — "Night Dispatch"

A dark-forward marketing theme, deliberately separate from Ledger Counter's
light paper/ink system, scoped entirely to the marketing route so the rest
of the app's tokens in `globals.css` are untouched.

- **Canvas:** near-black background (`hsl(228 20% 6%)`-range, not pure
  `#000`), warm off-white foreground text — a dark register the back-office
  never uses, so the marketing page reads immediately as a different kind
  of surface (front door, not workspace).
- **Accent:** one bright accent in Courier Cobalt's hue family (~230°),
  punched brighter for dark-background legibility — the same reasoning
  `AuthLifecycleGraphic`'s header comment already documents (a token tuned
  for a light card fails contrast on a dark panel; reuse the hue family,
  re-tune the lightness). This keeps a thread of continuity to the app's
  one brand hue without reusing a token verbatim in the wrong context.
- **Status hues as motion accents:** the existing `--status-*` tokens
  (dark-mode variants, already tuned for dark surfaces) appear only inside
  the animated product-mockup walkthrough (§4.3), where they carry their
  real operational meaning (in-transit amber, delivered teal, etc.) — never
  introduced as arbitrary decorative color elsewhere on the page.
- **Type:** same pairing as the app — Space Grotesk (display: hero
  headline, section titles) / IBM Plex Sans (body copy, nav, footer) — at a
  materially larger scale than any back-office usage (hero headline up to
  `text-6xl`/`text-7xl` at desktop), consistent with a marketing register.
- **Space:** generous negative space and full-bleed sections (unlike the
  back-office's dense `p-6` single-column convention) — each major section
  is its own full-viewport-width block with large vertical padding.
- **Scoping mechanism:** a `MarketingThemeProvider`/wrapper `<div>` around
  the whole page sets a local set of CSS custom properties (e.g.
  `--marketing-bg`, `--marketing-fg`, `--marketing-accent`) that marketing
  components reference — not new entries in `globals.css`'s `:root`/`.dark`
  blocks, and not a reuse of `.dark` class semantics (a logged-in user's
  light/dark preference must not affect this page, and vice versa).

## 4. Sections

Composed in `src/web/src/features/marketing/` as one component per section,
assembled by `app/page.tsx`. Each is independently testable (render + key
copy + any CTA `href`) per this project's existing component-test pattern.

### 4.1 `MarketingNav`
Sticky, minimal: wordmark ("Courier Platform" + the existing `Package`
lucide icon lockup, reused from the login page for identity continuity),
a few in-page anchor links (Features, How it works, Track a parcel), and
the auth-aware CTA (§2). `Track a parcel` links to `/track` (existing
public tracking entry, Phase F11) — the one real functional cross-link on
the page.

### 4.2 `Hero`
Headline + subhead stating the product's core claim (physical custody,
traceability — echoing `PRODUCT.md`'s Positioning section, not inventing
new claims), a primary CTA ("Sign in" / "Go to dashboard") and a secondary
CTA (anchor-scroll to the product walkthrough), and a compact preview of
the animated product-mockup sequence (full sequence lives in §4.5; the
hero shows its opening frame animating in as part of the hero's own
orchestrated entrance).

### 4.3 `FeatureGrid`
Six features, each with an icon, short title, one-sentence description —
staggered reveal on scroll:
1. Multi-branch & role-based access — organizations, branches, permission-based RBAC.
2. Scan-driven custody — barcode/QR receive, sort, dispatch, transfer.
3. Manifests & hub transfers — sealed manifests, branch-to-branch movement, reconciliation on receive.
4. COD lifecycle & settlement — collection through merchant settlement, fully traceable.
5. NDR & RTO management — structured non-delivery follow-up and return workflows.
6. Nepal-specific geography & pricing — Province/District/Local Level/Ward-driven zones and rate cards.

All six map to real, shipped backend/frontend capability (`PRODUCT-SPEC.md`,
`CLAUDE.md` §1) — no feature listed that doesn't exist.

### 4.4 `HowItWorks`
Four-step animated flow, each step revealing in sequence on scroll: **Book**
(shipment created with AWB) → **Scan & Sort** (branch receive, manifest,
transfer) → **Deliver & Prove** (rider assignment, delivery attempt, POD)
→ **Reconcile** (COD collection through merchant settlement).

### 4.5 `ProductWalkthrough`
The animated centerpiece: a stylized, non-functional mockup of the real
dashboard/tracking UI (reusing the app's actual `StatusBadge` visual
language and status color tokens for authenticity) animating a single
parcel through Created → In Transit → Out for Delivery → Delivered,
synchronized with the four `HowItWorks` steps' labels. All shipment/
recipient data shown is clearly fictional/placeholder-style (e.g. "AWB
NP2026-000000", "Sample Recipient") — never formatted to resemble a real
customer's data, per `PRODUCT.md`'s no-fabricated-evidence constraint.

### 4.6 `Credibility`
Replaces conventional social proof. Short section titled around "built for
how Nepal courier operations actually work," listing concrete domain
specifics already true of the product: Province → District → Local Level →
Ward geography (not free text), NPR-only settlement, real scan/manifest-
backed status transitions (no arbitrary admin status edits), audited
financial adjustments. No customer names, logos, review quotes, or
numeric usage claims ("500+ deliveries," etc.) anywhere.

### 4.7 `FinalCta`
Single, direct call to action mirroring the hero's primary CTA — "Sign in"
or "Go to dashboard" depending on auth state.

### 4.8 `MarketingFooter`
Minimal: wordmark, links to staff `/login`, merchant `/merchant-login`, and
public `/track`. No fabricated company details (address, social links,
etc.) that don't exist for this product yet.

## 5. Routing Change

`src/web/src/app/page.tsx` changes from a redirect-only server component to
a page that:

1. Calls `getValidAccessToken()` once, server-side (same helper the current
   redirect uses — no new auth logic).
2. Renders the marketing page, passing the resulting boolean/token presence
   down to `MarketingNav`/`Hero`/`FinalCta` as a simple `isAuthenticated`
   prop (or via a small server-computed value threaded through props —
   no client-side token check, consistent with `CLAUDE.md` §33's "never
   trust the frontend" principle applied to what *is* trusted here: this
   value only decides link text/href, never anything security-sensitive).

`middleware.ts` / any existing route protection for `/dashboard` and other
authenticated routes is unchanged — this phase only removes `/` itself from
the redirect chain. A user who navigates directly to `/dashboard` without a
valid session still gets whatever existing protection already applies
there (out of scope to touch).

`page.test.tsx` is rewritten: no more `redirect` assertions. New tests
render `RootPage` (mocking `getValidAccessToken` to resolve/reject) and
assert the nav CTA's text/href reflects each auth state.

## 6. Dependencies

- **Add `framer-motion`** (latest stable compatible with React 18/Next
  14.2) to `src/web/package.json`. Justification: the explicit animation-
  heavy requirement needs scroll-linked reveals, staggered children, and
  orchestrated multi-step sequences that plain CSS keyframes/
  `IntersectionObserver` would require substantially more hand-rolled JS
  to achieve cleanly (confirmed with the user as the deliberate trade-off,
  §Context). Used exclusively inside `src/web/src/features/marketing/` —
  Next.js route-based code splitting keeps it out of every other route's
  bundle.
- No other new dependencies. No backend changes, no new environment
  variables, no new API calls.

## 7. Accessibility

- Full landmark structure (`<nav>`, `<main>`, section `<h2>`s under one
  page `<h1>` in the hero, `<footer>`) and correct heading order —
  verified with a component test asserting heading levels don't skip.
- All motion respects `prefers-reduced-motion: reduce` — Framer Motion's
  `useReducedMotion` hook gates every animated variant, falling back to
  the fully-visible end state with no transition, mirroring
  `AuthLifecycleGraphic`'s existing CSS-level fallback.
- Color contrast on the new dark "Night Dispatch" tokens checked against
  WCAG AA before shipping (same standard `PRODUCT.md` already makes
  binding for the rest of the app) — text/accent combinations verified
  with the dataviz skill's contrast validator, same tool this codebase's
  existing color passes used.
- All interactive elements (nav links, CTAs, footer links) keyboard-
  reachable and focus-visible, consistent with the rest of the app's
  `focus-visible:ring` convention.

## 8. Testing

- **Component tests** (Vitest + Testing Library) per section: renders,
  key copy present, CTA `href`s correct for both auth states where
  applicable.
- **`page.test.tsx`** rewritten per §5: both auth states render the
  marketing page (no redirect), nav CTA reflects the correct state.
- **Reduced-motion test:** with `useReducedMotion` mocked/forced true,
  assert animated sections render in their final visible state
  immediately (no `opacity-0`/hidden initial state left stuck).
- **Accessibility test:** heading-order/landmark assertions as in §7.
- **Build/lint:** `npm run build`, `npm run lint`, full `npm test` suite
  green, including every pre-existing test (no regressions).

## 9. Non-Goals (explicit deferrals)

- **No lead-capture/contact form or backend.** CTAs link to existing
  `/login` only, per the confirmed decision — no new form, no email
  sending, no new API route.
- **No brand rename.** "Courier Platform" stays the working name; a real
  brand name is a separate, later decision (would touch this page's copy
  in only a handful of spots, by design — see §Context).
- **No changes to `/login`, `/merchant-login`, `/dashboard`, `/track`, or
  any other existing route's internals** beyond the `/` redirect removal.
- **No SEO/meta-tag/OG-image work, no analytics integration, no A/B
  testing infrastructure** — out of scope for this phase; can follow in a
  later, separately-scoped pass if wanted.
- **No CMS or copy-editing workflow.** All copy is hardcoded in the
  component files, same as every other static-copy surface in this app
  today.
