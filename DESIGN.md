---
name: Courier Platform
description: A muted, ledger-grounded design system for a Nepal domestic courier back-office — status-driven, dense, and built for scan-heavy operational work.
colors:
  paper: "#fffdf9"
  sand: "#f4f1ea"
  surface-sunken: "#f1eee7"
  console-rail: "#161821"
  console-rail-muted: "#8a8da0"
  ink: "#161821"
  courier-cobalt: "#1d2863"
  cobalt-tint: "#e9ebf7"
  slate-mist: "#e9e9ed"
  fog: "#eeeff1"
  fog-ink: "#5b5d68"
  terracotta: "#a83824"
  hairline: "#dbdce1"
  focus-ring: "#1d2863"
  status-neutral: "#75778a"
  status-in-transit: "#b8791f"
  status-out-for-delivery: "#e8a93c"
  status-delivered: "#0f766e"
  status-ndr: "#c8442d"
  status-rto: "#7a4b52"
  status-sealed: "#2857bd"
  status-received: "#0db9f2"
  marketing-bg: "hsl(228 20% 97%)"
  marketing-bg-elevated: "hsl(228 18% 93%)"
  marketing-fg: "hsl(229.1 20.0% 10.8%)"
  marketing-muted-fg: "hsl(230.8 6.7% 38.2%)"
  marketing-accent: "hsl(236.0 73.0% 46.0%)"
  marketing-border: "hsl(228 15% 55%)"
typography:
  display:
    fontFamily: "Space Grotesk, IBM Plex Sans, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "normal"
  body:
    fontFamily: "IBM Plex Sans, Arial, Helvetica, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  label:
    fontFamily: "IBM Plex Sans, Arial, Helvetica, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "normal"
  data-mono:
    fontFamily: "IBM Plex Mono, monospace"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
rounded:
  sm: "2px"
  md: "4px"
  lg: "6px"
  card: "12px"
  full: "9999px"
spacing:
  xs: "8px"
  sm: "12px"
  md: "16px"
  lg: "24px"
components:
  button-primary:
    backgroundColor: "{colors.courier-cobalt}"
    textColor: "{colors.paper}"
    rounded: "{rounded.lg}"
    padding: "8px 16px"
  button-primary-hover:
    backgroundColor: "{colors.courier-cobalt}"
  button-secondary:
    backgroundColor: "{colors.slate-mist}"
    textColor: "{colors.ink}"
    rounded: "{rounded.lg}"
  button-outline:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.lg}"
  status-badge:
    backgroundColor: "{colors.slate-mist}"
    textColor: "{colors.ink}"
    rounded: "{rounded.md}"
---

# Design System: Courier Platform

## Overview

**Creative North Star: "The Ledger Counter"**

This is the visual language of a courier branch counter's paper ledger, digitized: a warm, unbleached paper background, deep-indigo ink for text, a confident cobalt-blue brand accent for everything actionable, and a restrained set of status colors that behave like rubber-stamp categories, not marketing accents. It exists to be read quickly and correctly under time pressure by dispatchers, counter staff, and finance clerks — not to persuade or delight. Density and legibility come first; the main "expression" the system allows itself is Space Grotesk's slightly more geometric, confident display face on titles, section headers, and the auth-gate surfaces (login, register, accept-invite, session-expired), plus Courier Cobalt carrying every interactive surface, which keeps the interface from reading as generic gray SaaS while staying almost entirely out of the way in working content.

The palette is deliberately calmer than a typical categorical status system: every status hue was tuned to clear WCAG AA contrast and CVD-safe adjacent-pair separation first, vivid-and-legible-as-a-swatch second (`src/app/globals.css`'s header comment; verified with the dataviz skill's contrast validator). Status is never carried by color alone — every colored dot or badge ships with its text label next to it, because color is a confirmation signal here, not the message itself. Courier Cobalt, the interactive/brand hue introduced in the 2026-09-02 color pass, was checked the same way against every status hue before shipping — see the Colors section and `globals.css`'s header comment for the one accepted trade-off (dark-mode separation from Sealed/Received).

Two rejections are explicit and confirmed, not just absent: no camera/scanner chrome or courier-brand marketing gloss (this is Operate-mode software, not a storefront for the courier company), and no invented brand *name* — the product has no confirmed market name or logo yet (`PRODUCT.md`), so nothing here claims to be a logo or wordmark. Courier Cobalt is a color decision, not a naming one, and doesn't change that white-label status.

This system is still mid-build (Phases F0–F4 of nine staff-portal phases; see `docs/FRONTEND-ROADMAP.md`), and it shows small, real seams rather than one seamless language, called out honestly below instead of papered over — see Colors' Named Rules and the Components section for specifics as they're found and closed.

**Key Characteristics:**
- Warm paper background + deep indigo ink, not black-on-white or cool gray-on-white.
- Courier Cobalt — a single brand-blue hue, sharing ink's hue family — carries every interactive surface: primary buttons, links, focus rings, and KPI-highlight accents.
- A muted, WCAG-AA-first status palette — legible over loud, colorblind-safe over saturated.
- Space Grotesk display type for titles, section headers, and the two auth surfaces; IBM Plex Sans everywhere else, including Sheet titles.
- Almost flat: one soft default shadow for in-page surfaces, a second tinted lift reserved for the two auth surfaces, no elevation ladder beyond that.
- Every detail/create view opens as a right-side Sheet, never a routed page.

## Colors

A warm, low-saturation ledger palette: paper and ink for structure, a small set of desaturated status hues for meaning. Values below are light mode (`:root` in `globals.css`), the shipped default; dark mode (`.dark`) independently re-tunes every token's lightness rather than inverting it mechanically — see the Dark Mode note at the end of this section.

### Primary
- **Deep Indigo Ink** (`#161821`): body text on paper, and the `foreground` role generally — headings, table cells, form values. Ink is a neutral now, not a stand-in for a brand color.
- **Courier Cobalt** (`#1d2863`, light mode): the system's one brand hue, introduced in the 2026-09-02 color pass. Shares ink's ~230° hue family — deliberately "ink brought to life" rather than an unrelated accent — and carries the `primary` role (button fills), `ring` (focus rings), and (via `accent`, below) hover/selection tints and KPI-highlight panel backgrounds. Text/links/buttons on top of it use `--primary-foreground` (`#faf7f2`) for a contrast ratio above 12:1. This is the one token that changes hue between themes rather than just lightness — dark mode uses a brighter `#4f58e3` (see Dark Mode below) since a dark-lightness fill would disappear against the dark background.

### Secondary
- **Cobalt Tint** (`#e9ebf7`): the `accent` role — hover/active backgrounds (menu items, dropdown items, selection), `::selection`, and the background of highlighted KPI callout panels (e.g. Merchant Payable). A pale wash of Courier Cobalt, not a saturated highlight — replaces the previous "Warm Marigold Tint," which was disconnected from the (then ink-based) interactive palette. Marigold is retired from this role entirely; nothing else in the system references it.
- **Cool Slate Mist** (`#e9e9ed`) / **Fog** (`#eeeff1`): `secondary` and `muted` surfaces — secondary buttons, badge backgrounds, neutral icon-chip backgrounds, dropdown separators, disabled/quiet regions.

### Neutral
- **Sand** (`#f4f1ea`): the page `background` (light mode) — the warm ground every card sits on (F15 R1).
- **Lokta Paper** (`#fffdf9`): the `card`/`popover` fill (light mode) — a near-white surface lifted off the sand page. Named for the handmade Nepali paper it evokes, not a generic off-white.
- **Surface Sunken** (`#f1eee7`): `--surface-sunken` — table header rows and Sheet footer bars, one step below the card surface.
- **Fog Ink** (`#5b5d68`): `muted-foreground` — secondary/caption text, labels, timestamps, placeholder text.
- **Hairline** (`#dbdce1`): `border`/`input` — the only separator weight in the system; there is no secondary "heavier" border.
- **Terracotta** (`#a83824`): `destructive` — error text, destructive buttons, error-state borders/backgrounds (always at reduced opacity for backgrounds, e.g. `bg-destructive/5`).

### Status Palette (semantic, shipment/manifest lifecycle)
- **Status Neutral** (`#75778a`): Created, Cancelled — "nothing has happened yet" and "nothing more will happen," sharing one calm gray-blue rather than each earning a distinct hue.
- **Marigold — In Transit** (`#b8791f`, text-safe) / **Bright Marigold — Out for Delivery** (`#e8a93c`, dot/rail only): the motion family. Two lightness steps of the same hue, not two different hues — in-transit is the quieter "still moving," out-for-delivery is the brighter "about to arrive." This exact hue also serves as the highlighted "Merchant Payable" figure on the COD summary card — money owed reads as "still in motion," which is the same semantic the shipment-motion states use.
- **Deep Pine Teal — Delivered** (`#0f766e`): the one success color. Re-derived from an original forest-green in Phase F1 after it failed CVD adjacent-pair separation against NDR's vermillion — teal was chosen specifically to keep clearing that check while staying in the "success" family.
- **Vermillion — NDR** (`#c8442d`): non-delivery/failure. `DeliveryFailed` intentionally shares this exact token (backend auto-transitions it to NDR in the same request, so it is never a steady state worth its own color). Also used directly as a stat value's text color (e.g. an "Open NDRs" count on the branch dashboard renders in this hue, not neutral ink) when the number itself represents that status's count.
- **Muted Brick Rose — RTO** (`#7a4b52`): return-to-origin, a desaturated rose-brick distinct from both terracotta (destructive) and vermillion (NDR) at a glance.
- **Manifest Blue — Sealed** (`#2857bd`) and **Sky Cyan — Received** (`#0db9f2`): manifest-only statuses, namespaced separately from shipment statuses in `statusConfig.ts` since both state machines have their own `InTransit`.

### Chart Rank Ramp
A single-series bar chart whose bars are split by a category with no inherent color meaning — `BranchPerformanceChart` (volume by branch), `RiderPerformanceChart` (attempts by rider) — uses a 3-step single-hue ordinal ramp (`--chart-rank-1/2/3`, Courier Cobalt) instead of one flat color for every bar or an arbitrary categorical rainbow. Branches/riders have no "brand color" of their own and the X-axis already labels every bar directly (no legend to disambiguate), so a distinct hue per bar would be pure decoration, not information — a color strategy needs a relationship to hierarchy or meaning to earn its place. The ramp instead re-encodes magnitude: each bar is bucketed by its value relative to the series' own maximum (`rankFill()`, `features/reports/lib/rankFill.ts` — ≥66% of max, ≥33%, else lowest tier), so a set of near-equal values reads as near-equal color instead of an artificial 3-way split. Rank 1 (light mode) is the literal `--primary` value; rank 1 (dark mode) is a brighter cobalt than `--primary` itself, not the literal token — `--primary`'s dark value only clears 2.9:1 against `--card`, leaving no room below it in dark mode for two more steps that stay both visibly distinct (OKLCH ΔL ≥0.06) and legible (≥2:1, the ordinal-mark floor, not the stricter categorical-swatch floor since these are magnitude marks with axis labels, not unlabeled legend swatches) — verified with the dataviz skill's `validateOrdinal` check. Same hue (236° dark, 230° light) throughout, which is what "one brand hue" actually rests on here, not a shared exact value — this codebase already treats a bright-dot/dark-text pair of one status hue as legitimate for the identical reason (see the `--status-*` header comment in `globals.css`).

### Named Rules
**The Label-Always Rule.** No status is ever communicated by color alone. `StatusBadge` always pairs its colored dot with the status's text label, and any bare-dot use (funnel tiles) sits directly beside the same label — confirmed as the system's own mitigation for status hues that read calmer than a fully saturated categorical palette.

**The Tint-From-Token Rule.** A colored icon-chip or highlight-panel background is always an existing status or accent token at reduced opacity (`/15`) or the `accent` role, never a new ad hoc hex value introduced for one component. The chip's *shape* was unsettled between the org dashboard's rate tiles (`RateTiles`) and the branch dashboard's "Merchant Payable" highlight panel (`FinancialSummary`) until a 2026-09-11 audit pass standardized both on a rounded square (`rounded-lg`) — matching the neutral `h-9 w-9 rounded-lg bg-muted` section-header icon chip every dashboard Card already uses, so tinted and neutral chips now share one shape. `rounded-full` is no longer used for this treatment anywhere in the app.

### Dark Mode
Dark mode is a fully independent tuning, not an inversion: background `#141620`, foreground/ink `#f3efe7`, cards one step lighter than background (`#1e212e`). Status hues are re-lightened per-color rather than uniformly brightened — e.g. Delivered becomes a lighter teal (`#3fb39f`) and NDR a lighter vermillion (`#d45b3d`) — because a hue tuned for a light background does not carry the same contrast on a dark one. Courier Cobalt gets the same independent treatment, but shifts hue as well as lightness (`#1d2863` light → `#4f58e3` dark, 230°→236°): light mode gets its separation from Sealed/Received (both blue-family status hues) from a large lightness gap, but a dark-mode fill has to sit in the same bright range as those two status hues to clear 3:1 against the dark background, so it leans a few degrees further from Sealed's 221° instead. That's a real, checked trade-off, not an oversight — see `globals.css`'s header comment for the exact numbers and the re-validation command.

### Marketing System ("Night Dispatch")
The public marketing landing page (`/`, Frontend Phase F13) is the system's one confirmed second identity — a deliberately distinct palette for the one Persuade-mode surface the Ledger Counter world excludes by design (Product Principle 4: "clarity, density, and correctness outrank marketing polish," back-office only). It shares this system's type family (Space Grotesk display / IBM Plex Sans body via the same `font-display` class) and component primitives (Button, Sheet for its mobile nav drawer), but never the ledger's warm paper-and-ink palette — `--marketing-*` is its own indigo-neutral hue family (228° neutrals, 236° accent) so it reads as a distinct "night dispatch console" moment rather than a re-skinned dashboard.

- **Marketing Bg** (`hsl(228 20% 97%)` light / `hsl(228 20% 6%)` dark) and **Marketing Bg Elevated** (`hsl(228 18% 93%)` / `hsl(228 18% 10%)`): page background and raised panel fill (e.g. `AppScreenshotFrame`'s browser-chrome frame) — a cool near-black in dark mode, not the ledger's warm paper even when light.
- **Marketing Fg** (`hsl(229.1 20.0% 10.8%)` / `hsl(40.0 33.3% 92.9%)`) and **Marketing Muted Fg** (`hsl(230.8 6.7% 38.2%)` / `hsl(232.5 9.8% 67.8%)`): body/heading and secondary text, reusing the app's own light `--foreground`/`--muted-foreground` and dark `--foreground` literal values respectively rather than inventing new ones.
- **Marketing Accent** (`hsl(236.0 73.0% 46.0%)` / `hsl(236.0 73.0% 60.0%)`): the one interactive/emphasis hue — CTA buttons, links, the hero's glow accents, `MarketingThemeToggle`'s active state. Same 236° hue family as Courier Cobalt, underscoring it's one brand, two rooms rather than an unrelated palette.
- **Marketing Border** (`hsl(228 15% 55%)` / `hsl(228 15% 44%)`): the only separator weight on the page, same hairline-only convention as the rest of the app.
- **Marketing tones** (neutral/motion/success/alert, used only as `ProductWalkthrough`'s dot/rail fills, never as text): reuse this system's own already-verified `--status-created`/`-in-transit`/`-delivered`/`-ndr` values rather than inventing new hexes — one more instance of the app's standing rule that a new semantic color is sourced from an existing verified token first.

Every marketing color was computed and verified against WCAG AA (4.5:1 text, 3:1 non-text) in both themes — see `globals.css`'s Phase F13 header comment for the exact ratios. `MarketingThemeToggle` follows the same `next-themes` state as the rest of the app (a visitor's theme choice on `/` carries through to `/login` and beyond); only the fixed `--auth-panel` tokens stay intentionally theme-independent. A fixed-opacity noise-grain overlay (`.marketing-grain-overlay`, `opacity: 0.035`, `pointer-events: none`) is the one texture treatment anywhere in the system, reserved for this page.

**The Two-Room Rule.** Marketing and the ledger back-office never borrow each other's palette. A `--marketing-*` token never appears in `(staff)`/`merchant`/`platform`/`rider` surfaces, and the ledger's paper/ink/status tokens never appear on `/`. Shared type family and component primitives are the seam that keeps them one product; the color system is the seam that keeps them two distinct rooms.

**Custody Thread (F14).** The marketing pages share one structural motif: a single vertical rail (`ThreadLayout`) runs beside the sections and each section is a numbered `Checkpoint` (Plex Mono numerals, `01`–`06`) on it — the page reads like a parcel's scan history. The rail's accent overlay is scroll-linked (`scaleY`), fully drawn under `prefers-reduced-motion`, and `aria-hidden`. Tokens are unchanged; the deltas are shape and type:

- **Shape:** radii are `rounded-sm`/`rounded-md`/`rounded-lg` (4–8px), never `rounded-xl` or pills on marketing cards. Depth comes from hairline `--marketing-border` borders and a hard offset shadow (`shadow-[6px_6px_0_0_hsl(var(--marketing-accent)/0.18)]`), not blur or glow. No `backdrop-blur` on marketing surfaces.
- **Type:** Space Grotesk display headings scale up to `clamp(2.75rem, 8vw, 6.5rem)` on the hero with tight tracking; IBM Plex Mono (`font-mono`) is reserved for AWBs, checkpoint numerals, stamps, and eyebrow labels (`MonoLabel`), always uppercase with `tracking-[0.14em]`.
- **Diagrams:** explanatory graphics (custody chain, COD waterfall, manifest reconcile) are inline SVG generated from data and pure helpers in `features/marketing/lib/diagramMath.ts`; they are `aria-hidden` with an sr-only text equivalent inside a labelled `<figure>`, and use only `--marketing-*` and `--marketing-tone-*` colors. Color is never the only carrier of meaning (missing manifest cells are X-marked; nodes differ by fill).
- **Mock cards:** `MockCard` shows fictional sample data and always carries a visible "Sample data" tag. Never fabricate metrics, customers, or testimonials.
- **Motion:** transform/opacity only — thread scale, one-time `CountUp`, `Reveal`, and the CSS `.marketing-scan-line` sweep. Every one has a fully visible static fallback under `prefers-reduced-motion`.
- **Shared surfaces:** `/`, `/pricing`, `/track`, and `/track/[awb]` use these primitives; the auth showcase panel receives only a mono sample-AWB stamp and the larger headline, staying on its fixed `--auth-panel-*` tokens.

## Typography

**Display Font:** Space Grotesk (with IBM Plex Sans, sans-serif fallback)
**Body Font:** IBM Plex Sans (with Arial, Helvetica, sans-serif fallback)
**Label/Mono Font:** IBM Plex Mono (monospace) — reserved for AWB/tracking-number/manifest-code data: the shipment list and detail Sheet title, manifest scan lists and detail Sheet title, and `CommandPalette`'s entity-search results, plus the public tracking page and merchant API keys. Do not introduce a second monospace family for that purpose.

**Character:** Space Grotesk's slightly geometric, wider-set forms give page titles, section headers, and the auth-gate surfaces a touch of confidence and structure; IBM Plex Sans's plainer, highly legible forms carry every sentence of actual operational content. The pairing reads as "considered software," not generic or decorative.

### Hierarchy
- **Page Title** (Space Grotesk, 600, `text-xl` / 1.25rem, tight line-height): one per page, top-left, e.g. "Shipments," "Branches."
- **Section/Card Title** (Space Grotesk, 600, `text-base` / 1rem): paired with a small icon chip (see Components).
- **Auth Surface Title** (Space Grotesk, 600, `text-xl`/`text-lg`): the Login card's "Sign in," Register's and Accept-Invite's form titles, and the session-expired Dialog's "Session expired" — the only overlay/standalone-card titles that opt into the display face, marking every account-gate screen as a distinct, higher-stakes moment rather than ordinary in-app content.
- **Sheet Title** (IBM Plex Sans, 600, `text-lg` / 1.125rem): every Sheet consistently keeps the plain body face — Sheets read as an extension of the underlying page's content, not a new "brand moment," unlike the two auth surfaces above.
- **Body** (IBM Plex Sans, 400, `text-sm` / 0.875rem): the default for table cells, form values, descriptions.
- **Stat Value** (IBM Plex Sans, 600, `text-lg`–`text-2xl` depending on context): tile numbers (rate tiles, funnel counts, exception counts) — the one place body-family text gets pushed large and bold instead of switching to display. Occasionally takes a semantic status color instead of ink (an NDR count in vermillion) when the number itself embodies that status.
- **Label/Caption** (IBM Plex Sans, 400, `text-xs` / 0.75rem, `muted-foreground`): field labels' sibling captions, stat-tile labels, table pagination text, card descriptions, record-field labels.
- **Micro Label** (IBM Plex Sans or Mono, `text-2xs` / 0.6875rem = 11px, line-height 1rem): the one project step below `text-xs`, defined in `tailwind.config.ts` — sidebar group headers, badge/chip text, step numbers, dense mono captions. Nothing smaller.

### Named Rules
**The On-Scale Rule.** Font sizes come only from the scale above (`text-2xs` through `text-2xl`); no arbitrary `text-[13px]`-style values. A size the scale lacks is a DESIGN.md change first, not a one-off class.

**The Sheet-Stays-Plain Rule.** Sheet titles use the plain body face, not Space Grotesk, with no exceptions in the shipped code — Sheets are working surfaces layered over a page, not their own destination that needs a brand moment. The session-expired Dialog is a different surface type (a full-screen gate, not a task panel) and does opt into the display face; don't generalize its choice back onto Sheets.

## Layout

A single main content column per page (`p-6` on the content region, `space-y-4`/`space-y-6` between sections), with a fixed collapsible icon-rail sidebar (`AppSidebar`, `collapsible="icon"`) and a sticky `h-16` header (a visual-only search placeholder, theme toggle, user menu) — never a second, competing top nav. Below `md`, the sidebar collapses entirely into a `Sheet`-based drawer opened from a hamburger trigger in the header; there is no intermediate tablet-specific layout, just the mobile drawer breakpoint and the desktop rail. Dashboard and stat-tile grids step from 1–2 columns (mobile, `grid-cols-2`) to 3–4 (`sm`/`lg`) depending on how many tiles a section has — `grid-cols-2 sm:grid-cols-4` (rate tiles, exceptions) and `grid-cols-2 sm:grid-cols-3` (branch shipments) are both established, chosen by tile count rather than a single fixed grid. Two-column form layouts (create Sheets) also collapse to one column below `sm`. Vertical rhythm is `gap-4`/`space-y-4` (16px) between related fields and controls, `gap-6`/`space-y-6` (24px) between distinct sections or tiles, with a plain `border-t border-border` divider (no card-within-card nesting) separating sub-groups inside one Card, as in `ShipmentFunnel`'s two status rows.

## Elevation & Depth

The system is almost flat and leans on paper/ink contrast and hairline borders for structure rather than shadows, with exactly one deliberate exception. At rest, ordinary in-page surfaces use Tailwind's default `shadow` (Cards, primary/secondary/destructive buttons — a very soft, colorless ambient lift) or `shadow-sm` (Input, outline Button, dropdown/select popovers use `shadow-md`, the Sheet panel uses `shadow-lg`) — all colorless, all subtle. The **Login, Register, and Accept-Invite cards and the session-expired Dialog** are the one place the system reaches for something stronger: all four use `shadow-xl shadow-primary/5`, a wider, cobalt-tinted shadow that makes these "you are not inside the app yet" surfaces read as visibly more elevated than any ordinary dashboard card. Nothing elevates on hover; state changes are communicated by background-color shifts (`hover:bg-accent`, `hover:bg-primary/90`), not by adding depth — the one motion exception is a small icon-translate on the login button (see Components → Buttons).

### Shadow Vocabulary
- **Ambient rest** (`shadow`): Cards, primary/secondary/destructive buttons at rest.
- **Ambient subtle** (`shadow-sm`): Input, Select trigger, outline Button.
- **Overlay** (`shadow-md`/`shadow-lg`): dropdown/select popovers, the Sheet panel — reserved for surfaces that float above the page.
- **Auth-gate lift** (`shadow-xl shadow-primary/5`): the Login, Register, and Accept-Invite cards and the session-expired Dialog only — a wider, cobalt-tinted shadow signaling "this is the one thing on screen right now," not used anywhere else.

### Named Rules
**The Flat-Ledger Rule.** Depth exists only to separate an overlay (Sheet, dialog, dropdown) from the page beneath it, or to mark the two authentication gate surfaces. A surface sitting in the normal document flow — card, tile, table row — never gains a shadow, tinted or otherwise, that it didn't already have at rest.

## Shapes

Two independent radius scales coexist deliberately. The base UI scale follows shadcn's `--radius` variable (`0.375rem` / 6px): `rounded-lg` = 6px (buttons, Sheet trigger, badges' outer shape via `rounded-md`, the Dialog panel), `rounded-md` = 4px (inputs, Select triggers, StatusBadge dot's `rounded-full`, small icon chips, the `⌘K` shortcut hint), `rounded-sm` = 2px (the Sheet's close button). Cards break from that scale on purpose, using a fixed `rounded-xl` (12px) — a visibly softer, more generous corner than any interactive control gets, so containers read as calmer resting surfaces and controls read as more precise, clickable ones. Round, fully circular treatments (`rounded-full`) are reserved for avatars, status dots, one rate-tile icon style, and the header's icon-only buttons (theme toggle) — everything else stays on the rectangular scale above. Borders are a single 1px hairline everywhere (`border-border`/`border-input`); there is no thick/heavy border weight anywhere in the system.

## Components

### Buttons
- **Shape:** `rounded-lg` (6px), consistent across every variant.
- **Primary:** Courier Cobalt (`#1d2863`) fill, paper text, soft default `shadow`; `hover:bg-primary/90`. The only button variant that reads as "the" action on a screen — previously an ink fill with no color identity of its own; the 2026-09-02 color pass gave it the system's one brand hue instead. The login page's submit button adds one micro-interaction not used elsewhere: its trailing arrow icon nudges right on hover (`group-hover:translate-x-0.5`) — a confirmed, deliberate one-off for the highest-stakes single action in the app, not a pattern to spread to every button.
- **Secondary:** slate-mist fill, ink text, `shadow-sm`, `hover:bg-secondary/80` — used for the second-priority action beside a primary (e.g. Previous/Next pagination is `outline`, not `secondary`).
- **Outline:** paper fill, hairline border, `hover:bg-accent` (cobalt tint) — the default for non-destructive secondary actions (Retry, pagination, Cancel).
- **Destructive:** terracotta fill, paper text — reserved for genuinely destructive/irreversible actions.
- **Ghost / Link:** no fill at rest; ghost gets the same `hover:bg-accent` treatment as outline (used for lower-emphasis actions like "Sign in as someone else"); link renders in Courier Cobalt (`text-primary`) and underlines on hover only — this is the one place the brand hue appears as text color, not just fill.
- **Icon-only:** square `icon` size (36px) by default (e.g. the sidebar trigger), but the header's theme toggle overrides this to `rounded-full` — the one confirmed case of a circular icon button; don't treat it as license to round every icon button.
- **Sizes:** `sm` (32px), `default` (36px), `lg` (40px), `icon` (36px square) — heights step in 4px increments.

### StatusBadge (signature component)
A neutral `secondary`-variant pill (slate-mist background, ink text, `rounded-md`) with a small (`h-2 w-2`) `rounded-full` dot in the status's token color, plus the status label as ordinary text — never a saturated color-filled pill. This exists specifically because several status hues aren't safe as text-on-fill at every lightness the app supports (light + dark), while a small dot only needs the lighter 3:1 non-text contrast bar. `resolveStatusConfig` in `statusConfig.ts` is the single source of truth mapping every `ShipmentStatus`/`NdrStatus`/`ManifestStatus` value to a label + token; a status not in that map falls back to the neutral/cancelled token rather than an undefined color.

### Cards / Containers
- **Corner Style:** `rounded-xl` (12px) — see Shapes' named exception.
- **Background:** near-white card on the sand page (`bg-card` over `bg-background`) — the two no longer share a value (F15 R1).
- **Shadow Strategy:** default Tailwind `shadow` for in-page cards; the tinted `shadow-xl shadow-primary/5` lift is reserved for the auth-gate cards (Login, Register, Accept-Invite) only (see Elevation).
- **Border:** 1px hairline (`border-border`).
- **Internal Padding:** `CardHeader`/`CardContent`/`CardFooter` all use `p-6`, content dropping the header's shared top padding (`pt-0`) so the two stack without doubled space.
- **Section header pattern:** every dashboard Card leads with a `CardTitle` that pairs a `text-base` Space Grotesk label with a `h-9 w-9 rounded-lg bg-muted` icon chip to its left, and (where the data has a time window) a `CardDescription` caption beneath naming it (e.g. "Last 30 days") — this pairing is the standard, not an exception; a bare text-only `CardTitle` on a data section is the deviation to avoid.

### Stat Tiles and Record Fields
Three distinct label/value patterns coexist, each tied to what the number means, not interchangeable:
- **Bare tile** (`ShipmentFunnel`, `BranchExceptions`, `BranchShipments`): a `text-xs text-muted-foreground` label above a bold `text-2xl` value, no border, no icon. The default for a grid of counts.
- **Bordered icon tile** (`RateTiles`, `FinancialSummary`'s per-field grid): a small neutral or tinted icon chip beside a `text-xs` label and `text-lg` value, inside a `rounded-lg border border-border` box. Used when each tile represents a genuinely distinct metric worth an icon, not just a count bucket.
- **Record Field** (`BranchRecord`'s `Field` helper): a `text-xs text-muted-foreground` label above a `text-sm font-medium` value — quieter than either stat pattern, used for descriptive facts (code, name, phone) rather than metrics.

A highlighted single-metric callout (`FinancialSummary`'s "Merchant Payable") escalates further: an `accent`-tinted panel (`bg-accent`, `rounded-lg`, `p-4`) with its own status-tinted icon badge and a larger (`text-xl`) status-colored value — reserved for the one number on a card that matters most.

### Inputs / Selects
- **Style:** transparent background, hairline border, `rounded-md`, `shadow-sm`, `h-9` (36px) — Input and SelectTrigger share this exact treatment.
- **Focus:** `focus-visible:ring-1 focus-visible:ring-ring` — a thin single ring, not a glow or border-color shift.
- **Icon-decorated input:** email/password fields on the two auth surfaces prefix an absolutely-positioned `h-4 w-4 text-muted-foreground` icon inside the field (`pl-9` on the input to clear it) — established on Login and the reauth Dialog; the password field additionally gets a right-aligned show/hide toggle (`Eye`/`EyeOff`). Not yet used on any non-auth form; adopt it there too if a field's purpose benefits from the icon, rather than treating it as auth-only by rule.
- **Error:** the input itself never changes color on validation failure; the shared `FieldError` component renders the message beneath the field instead, always leading with a small `AlertCircle` icon (`h-3.5 w-3.5`) — every form gets this from the one shared component, not a per-surface opt-in.
- **Labels:** `Label` (shadcn default) sits directly above its control with `space-y-2` (8px) between them; every field is a `Label` + control + optional error-message stack, never a floating/inline label.

### Sheets (signature interaction pattern)
Every detail and create/edit view in this app is a right-side `Sheet` (`side="right"`, `w-3/4 sm:max-w-sm`–`sm:max-w-xl` depending on content), never a routed page — driven by a per-feature Zustand store (`branchPanelStore`, `shipmentPanelStore`, `manifestPanelStore`) that tracks which panel/mode is open. `SheetContent` uses the base `p-6` padding, a `shadow-lg` + `bg-black/80` scrim, and slides in from the right over 500ms open / 300ms close. Forms inside a Sheet use `mt-6 space-y-4` from the `SheetHeader`, with two-column (`sm:grid-cols-2`) field pairs collapsing to one column on mobile. A manifest's single detail Sheet swaps its body content by lifecycle status (Draft/Sealed/InTransit/Received/Closed) rather than opening a different Sheet per state — the container is stable, the content inside it is state-driven.

### Dialogs
Reserved for app-level interruptions outside the normal page flow — today, only the non-dismissable session-expired `ReauthDialog` (no close button, `onInteractOutside`/`onEscapeKeyDown` both prevented, since a failed refresh token genuinely requires re-authentication before anything else can happen). Centered (`fixed`, translated to the viewport center), `max-w-lg`, `rounded-lg`, with the auth-gate tinted `shadow-xl shadow-primary/5` lift (see Elevation) and the same `bg-black/80` scrim as Sheets. Unlike a Sheet, a Dialog's title opts into the display face (`font-display`) — a deliberate difference, not an inconsistency (see Typography's Sheet-Stays-Plain rule).

### Tabs
Flat, ruled-underline tabs, not a rounded pill switcher — the one primitive that now shares the ledger's baseline-and-hairline vocabulary rather than default shadcn chrome. `TabsList` is a horizontally-scrolling flex row sitting on a single `border-b border-border` baseline (no background, no padding); `TabsTrigger` is muted-foreground text with a transparent 2px bottom border at rest, `hover:text-foreground` (ink) on hover, and Courier Cobalt text plus a 2px cobalt bottom border when active — the border overlaps the container's hairline via a `-mb-px` offset so the two never render as a doubled line. No shadow, no fill, ever: the active state is carried entirely by color and the underline, consistent with the Flat-Ledger Rule. The scrollable list (no wrapping) is deliberate for tab sets that grow additively (Admin, Settings sub-tabs, COD's six tabs) so new tabs degrade to a scroll on narrow viewports instead of wrapping or squeezing labels. The scroll container's native drag-bar scrollbar is hidden (`.scrollbar-hidden`, `globals.css`) rather than left at the browser default — wheel/trackpad/touch/keyboard scrolling all still work, only the visible platform widget is suppressed, following the same "theme every browser surface" rule as `::selection`.

### Menus (Dropdown)
The user menu is the only dropdown in the app today: an avatar + name trigger (`hover:bg-accent`, `rounded-full`) opening a `bg-popover` panel (`rounded-md`, hairline border, `shadow-md`, `min-w-56`) with a header row (avatar + name + email), a hairline separator, and one or more `DropdownMenuItem`s that share the sidebar's `focus:bg-accent` hover treatment. Avatars are initials-only (no image upload exists yet) on a `secondary`-tinted circle with a `ring-1 ring-border`.

### Page primitives
Shared page-level building blocks in `src/components/page/`: `PageHeader` (title, description, actions), `SectionCard` (titled card section), `KpiTile` (single metric), `PipelineStrip` (ordered stage counts), `SheetHeaderBlock` (Sheet title/identity block) and `ComingSoonSlot` (placeholder for data the API does not serve yet).

**The No-Fake-Numbers Rule.** Data the API does not serve yet renders `ComingSoonSlot` (`src/components/page/ComingSoonSlot.tsx`, `data-coming-soon`), never mock or sample values on a product screen.

### Navigation (Sidebar)
- **Style:** a dark console rail: `#161821` in light mode and one step darker than the page in dark mode (`--sidebar-background` `225 22% 7.1%`, ≈`#0e1016`). Muted text is `#8a8da0`. In the frontmatter, `console-rail` maps to `--sidebar-background` and `console-rail-muted` to `--sidebar-muted`; the brand-lockup chip uses `--sidebar-primary` (`#4f58e3`), with a static brand lockup at the top. One `ConsoleShell` (`components/console/`) renders the rail, header and content region for staff, merchant and platform consoles alike; each console supplies only its own nav groups.
- **Groups:** grouped, non-collapsible section headings in IBM Plex Mono caps. Headings are labels, not disclosure controls. Items are gated per console (staff by permission via `useStaffNavGroups`; merchant via `useMerchantNavGroups`) and render nothing when denied; a group with no permitted items is dropped.
- **Active state:** `bg-sidebar-accent` (`--sidebar-accent`, `#2a2e42`) with white text and `aria-current="page"`; the active item is the longest-matching href (`findActiveItem`).
- **Mobile:** below `md`, the rail is replaced by a `Sheet`-based drawer opened via a header hamburger trigger, sharing the same nav content as desktop so the two can't drift apart.

### Header
Sticky, `h-16`, hairline bottom border, `bg-card`: sidebar trigger, a breadcrumb (group › page, `ConsoleBreadcrumb`), a global search bar, then the AD/BS dual date (`DualDate`, `Asia/Kathmandu`), a notifications bell (`NotificationsButton`, showing a `ComingSoonSlot` until a notification feed exists), theme toggle + vertical hairline separator + user menu, right-aligned. The search bar (`⌘K`-labeled) opens `CommandPalette` — a real `Ctrl+K`/`⌘K` command dialog (`components/layout/CommandPalette.tsx`) listing every permitted nav page and permission-gated quick-create action, plus live entity search once a query is at least 2 characters (shipments, branches, manifests, NDRs, return shipments via `useGlobalSearch`). AWB/manifest-code results use the same reserved monospace treatment as the shipment list (see Typography). This replaced an earlier non-functional placeholder; nothing in the header is decorative-only anymore.

### Data Table
The table itself sits in its own bordered container — `rounded-xl border border-border bg-card` (`overflow-hidden` to clip the corners), the same corner radius as a Card, with a `bg-surface-sunken` header row so it reads as a lifted card on the sand page (F15 R1; it replaced the earlier `bg-muted/40` fill, needed only while card and page shared a value). `DataTable` accepts an optional `selectedRowId` to highlight the row whose Sheet is open. Header row + body rows keep the shadcn `Table` default 1px hairline row separators inside that container, no zebra striping, no row hover highlight beyond the default. Below the table, outside the bordered container: a plain-text "Page X of Y (N total)" caption on the left, numbered `PaginationLink`s with ellipsis collapsing plus Previous/Next controls (disabled at the bounds) on the right — this superseded an earlier Previous/Next-only design once real page counts made jump-ahead navigation worth the extra chrome. The bordered/rounded/tinted container itself is a standalone component, `BorderedTableContainer` (`components/data-table/`), promoted out of `DataTable` during the 2026-09-08 audit pass once a sixth hand-rolled copy of the same wrapper markup showed up across the app; `DataTable` and every non-`DataTable` table (`MerchantSettlementsTab` and the five merchant portal list pages — cod, kyc, pickups, staff, api-keys, none of which paginate since their list endpoints don't support it yet) now render through this one shared wrapper instead of copy-pasting the div.

### Scan station (F15 R2)
Camera-first continuous barcode scanning. Manual and keyboard-wedge input always available (live
input field under the camera view, unfocused between scans). One result card with `aria-live="polite"`
per scan (accepted = teal tint + check icon; rejected = destructive tint + server message). Session
log (newest first, time · AWB · mode · result) beneath. Mode switcher (Tabs) showing only available modes; unavailable modes hidden, not disabled.

### Progress stepper (F15 R2)
Seven forward steps (Booked, Picked up, Origin hub, In transit, Destination, Out for delivery, Delivered);
text labels always present beside or below icons (not color-only). Exception markers (NDR symbol, RTO symbol,
or Cancelled) appear after the last forward step reached; NDR/RTO statuses never show a fake Delivered step.
Current step carries `aria-current="step"`. Step progression is derived from the shipment's `currentStatus`
alone; complete, current and upcoming states distinguished by icon color and opacity.

### "Right now" custody card (F15 R2)
Cobalt-tinted section (`bg-cobalt-tint`, `rounded-lg`) at the top of the shipment Sheet detail view.
Shows: Custodian (rider display name or staff name or "—"), Owning branch (current branch name or
"In transit"), In custody since (timestamp + elapsed duration like "2 h 18 m", or "—"), Destination branch.
Derived from the most recent `ShipmentEvent` whose branch/user/rider holder differs from the prior event.

- **Money-flow strip** — six cumulative COD stages as connected cards (step number, label, `Rs` amount, parcel count); exactly one card highlighted cobalt: the stage before the largest drop. Cards switch to the tab that moves that money on. Loading = skeleton, error = ErrorState — never zeros.
- **Variance wording** — always text: "Matched", "Short − Rs N", "Over + Rs N"; the status-colour dot is secondary. Previews computed in the browser are labelled "(preview)".

### Empty / Loading / Error States
- **Empty:** dashed hairline border, `rounded-lg`, generous `p-12` padding, centered content — an optional circular `bg-muted` icon chip, a Space Grotesk `text-sm font-medium` title, an optional `muted-foreground` description, and an optional action below.
- **Loading:** a stack of `Skeleton` bars (`h-10 w-full`, default 5 rows) inside the same `rounded-xl border border-border bg-card p-3` container the populated table uses, standing in for the eventual table/list shape — the container previews the exact shape the table pops into once data arrives, rather than skeleton bars sitting flush with the page and the framed card appearing out of nowhere on load. Never a full-page spinner for list content (a bare `Spinner` is reserved for page-level "nothing has rendered yet" waits, e.g. the shipments list's initial load).
- **Error:** terracotta-tinted (`border-destructive/30 bg-destructive/5`) variant of the same centered `p-12` card shape, terracotta title text, optional Retry (`outline` button).

## Do's and Don'ts

### Do:
- **Do** use Space Grotesk for page titles, card/section titles, and the auth-gate surfaces (login, register, accept-invite, session-expired); every other piece of text — including Sheet and dropdown content — stays in IBM Plex Sans.
- **Do** pair every status color with its text label (dot + label in `StatusBadge`, dot + label in funnel tiles) — never color as the sole signal.
- **Do** source any tinted icon-chip or highlight-panel background from an existing status or accent token at reduced opacity, never a new hex value.
- **Do** open every detail/create/edit view as a right-side Sheet backed by a Zustand panel store; reserve routed pages for list/index views, and the centered Dialog exclusively for app-level interruptions like session expiry.
- **Do** verify a new status or semantic color against WCAG AA contrast and CVD-safe adjacent-pair separation before adding it (the dataviz validator this system was built with) — this is a binding standard per `PRODUCT.md`, not best-effort.
- **Do** hide a nav item or gated action entirely (`return null`) when the user lacks the permission, rather than showing it disabled.
- **Do** extend `CommandPalette`'s entity search (`useGlobalSearch`) to new record types as they ship, rather than building a second, page-local search input — it's the one search surface in the app.

### Don't:
- **Don't** add a shadow to an at-rest surface (card, tile, table row) that doesn't already have one — depth is reserved for overlays (Sheet, dialog, dropdown) and the two auth-gate surfaces only.
- **Don't** introduce a second monospace or display typeface; IBM Plex Mono is already reserved (unused) for future AWB/tracking-code data, and Space Grotesk is the only display face.
- **Don't** give Cards the base 6px `--radius` scale; their 12px `rounded-xl` is an intentional, confirmed exception, not a value to "fix" into consistency with buttons/inputs.
- **Don't** invent a brand name, logo, or marketing personality for this product — it is confirmed unnamed/white-label (`PRODUCT.md`); keep the system generic until that changes.
- **Don't** make section headings collapsible or add sub-items — headings are labels, not groups.
- **Don't** reintroduce a `rounded-full` tinted-emphasis icon badge for a stat/metric tile — `RateTiles` and `FinancialSummary` disagreed on this until a 2026-09-11 audit pass standardized both on `rounded-lg`, matching the rest of the icon-chip system; `rounded-full` stays reserved for avatars, status dots, and the header's icon-only buttons (see Shapes).
- **Don't** assume the ledger palette applies to the public marketing landing page — `/` intentionally runs a separate, distinct `--marketing-*` token system ("Night Dispatch"; see Colors → Marketing System) as the one confirmed exception to Product Principle 4.
