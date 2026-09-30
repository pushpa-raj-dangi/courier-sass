# F15 Console Redesign — R1: Console Foundation — Design Spec

## 1. Context

A visual redesign of the whole authenticated app was explored on a design canvas
(claude.ai artifact "Courier Platform UI Redesign",
https://claude.ai/artifact/1dGwukUsc2jwxhThg5eyHu): a dark console rail, a card-on-sand
surface system, a parcel pipeline dashboard, a shipment Sheet led by a "Right now" custody
card, a staff Scan station, a COD money-flow view, a rider run + POD flow, and public
tracking. The user approved implementing it across **all** surfaces (staff, rider, merchant,
platform) as **frontend-only** work; the backend changes later.

The redesign is decomposed into six sequenced sub-projects, each with its own
spec → plan → implementation cycle (recorded as Frontend Phase F15 in
`docs/FRONTEND-ROADMAP.md`):

| Phase | Scope |
|---|---|
| **R1 — Console Foundation** (this spec) | Tokens, shared `ConsoleShell` (staff/merchant/platform), grouped dark sidebar, new header, page primitives, `DataTable`/`StatusBadge` restyle, `ComingSoonSlot` |
| R2 — Staff operations | Dashboard, Shipments list + detail Sheet, new `/scan` Scan station |
| R3 — Staff finance & network | COD, manifests, NDR, RTO, riders, live map, branches |
| R4 — Rider PWA | Run screen, POD flow, scan, profile |
| R5 — Merchant portal + public tracking | 7 merchant pages, `/track` |
| R6 — Platform + admin | 9 platform pages, admin, audit, billing, auth pages |

### Standing decisions (apply to R1–R6)

- **Frontend only.** No API, DTO, migration or backend change in any R-phase.
- **Missing data → `ComingSoonSlot`.** A design element whose data the API does not serve
  yet (hourly flow, "Needs action" counts, sort bins, rider cash-in-hand, denomination
  counts, notifications, nav badge counts) is built as a real, labelled placeholder slot —
  never mock or invented numbers on a production screen. Each slot is listed as a backend
  follow-up in its phase's spec.
- **Approach:** one shared shell + retuned tokens (option 1 of 3 considered). Rejected:
  restyling three shells in place (keeps three drifting copies) and a parallel v2 component
  set (two design systems coexisting through R2–R6).

## 2. Goals / Exit Criteria

- Staff, merchant and platform consoles all render inside one `ConsoleShell` with the
  dark grouped sidebar and the new header, in light and dark themes, and at 375 px width
  with no horizontal page scroll.
- New surface tokens applied: sand page background, near-white cards, sunken surface,
  dark console rail.
- Page primitives exist in `src/components/page/` and are used by at least the dashboard
  header (reference adoption); other pages adopt them in R2–R6.
- `DataTable`, `BorderedTableContainer`, `StatusBadge`, `EmptyState`, `LoadingState`
  restyled in place — every page picks the change up with no behaviour change.
- `npm run build`, `npm run lint`, `tsc --noEmit`, `npm test` all pass.
- `DESIGN.md` and `docs/FRONTEND-ROADMAP.md` updated.

### Non-goals

- Page-level layout rework of any page other than the dashboard header (R2–R6).
- The Scan station route and its nav item (R2).
- Sidebar "plan usage" card (plan usage is not in the session payload; backend follow-up).
- Any change to `--status-*`, `--marketing-*`, `--auth-panel*` tokens, fonts, or radii.
- Marketing (`/`, `/pricing`), public `/track`, and auth pages are untouched.
- New npm dependencies (none needed: shadcn Sidebar, Popover, lucide, `bsDate.ts` exist).

## 3. Tokens (`src/web/src/app/globals.css`)

Light mode changes; everything else unchanged.

| Token | Current | New | Reason |
|---|---|---|---|
| `--background` | `37.5 44.4% 96.5%` (#faf7f2) | `42 31% 93.7%` (#f4f1ea) | Page must differ from cards |
| `--card`, `--popover` | = background | `40 100% 98.8%` (#fffdf9) | Cards lift without shadow (Flat-Ledger Rule holds) |
| `--surface-sunken` (new) | — | `42 26% 92.5%` (#f1eee7) | Table header row, Sheet footer bar |
| `--sidebar-background` | paper | `229 20% 10.8%` (#161821) | Dark console rail |
| `--sidebar-foreground` | ink | `231 12% 79%` (#c3c5d0) | Rail item text |
| `--sidebar-accent` | cobalt tint | `230 22% 21.2%` (#2a2e42) | Active item fill |
| `--sidebar-accent-foreground` | ink | `0 0% 100%` | Active item text |
| `--sidebar-primary` | cobalt | `236 73% 60%` (#4f58e3) | Brand chip, badges on ink |
| `--sidebar-primary-foreground` | paper | `0 0% 100%` | |
| `--sidebar-border` | hairline | `229 20% 21.6%` (#2c3042) | Scope switcher, separators |
| `--sidebar-muted` (new) | — | `232 10% 58.4%` (#8a8da0) | Group headings |
| `--sidebar-ring` | cobalt | `236 73% 70%` | Focus ring visible on ink |

Dark mode: `--background`/`--card` unchanged; `--surface-sunken` = `229 20% 12.5%`;
sidebar one step darker than the page, `--sidebar-background` = `225 22% 7.1%` (#0e1016),
other sidebar values as light mode. Add `surface-sunken` and `sidebar.muted` to
`tailwind.config.ts`.

Every new text/non-text pair is checked with the dataviz contrast validator (≥4.5:1 text,
≥3:1 non-text) and the ratios recorded in the `globals.css` header comment, as existing
tokens do. Expected: muted-foreground on new background ≈5.9:1, on card ≈6.5:1;
`--sidebar-muted` on rail ≈4.6:1; `--sidebar-foreground` on rail ≈10:1. Any pair that fails
is re-tuned in lightness only before shipping.

## 4. `ConsoleShell` (`src/web/src/components/console/`)

### 4.1 API

```ts
interface ConsoleNavItem {
  href: string;
  label: string;            // already translated by the caller
  icon: LucideIcon;
  badge?: ReactNode;        // optional; not wired to data in R1
  matchPrefix?: boolean;    // active when pathname startsWith(href)
}
interface ConsoleNavGroup { id: string; label: string; items: ConsoleNavItem[] }

interface ConsoleShellProps {
  brand: { icon: LucideIcon; name: ReactNode; subtitle?: ReactNode; isLoading?: boolean };
  scope?: ReactNode;         // staff: BranchSwitcher
  nav: ConsoleNavGroup[];    // ALREADY permission-filtered by the caller
  navLoading?: boolean;      // renders skeleton rows
  headerStart?: ReactNode;   // staff: HeaderSearch
  headerEnd?: ReactNode;     // language/theme toggles, user menu
  banners?: ReactNode;       // Impersonation/Trial banners above header
  children: ReactNode;
}
```

Permission filtering stays in each surface (staff `usePermission`, merchant's own checks,
platform none), producing plain `ConsoleNavGroup[]`. `ConsoleShell` drops any group with zero
items. Built on the shadcn `Sidebar`/`SidebarProvider`/`SidebarInset` (icon-collapse, mobile
Sheet drawer, rail unchanged). Mobile drawer closes on navigation (current behaviour).

### 4.2 Sidebar

Brand chip (34 px, `--sidebar-primary` fill, white icon) + name (Space Grotesk 15/600) +
optional subtitle; scope slot; groups with Plex Mono uppercase 10.5 px headings
(`tracking-[0.12em]`, `text-sidebar-muted`, hidden when collapsed); items 38 px min height,
18 px icon, 13.5 px label. Active item: `bg-sidebar-accent text-sidebar-accent-foreground
font-medium`, `aria-current="page"` — no left-border stripe. Collapsed mode keeps the
existing tooltip label.

### 4.3 Header (`ConsoleHeader`)

64 px, `bg-card`, hairline bottom border, sticky. Left→right: `SidebarTrigger`;
`ConsoleBreadcrumb` (group › page, derived from the `nav` prop + pathname; hidden < `sm`);
`headerStart`; right-aligned: `DualDate` (hidden < `lg`), `NotificationsButton`,
`headerEnd`.

- **`DualDate`**: AD weekday/date plus the BS date (e.g. "Wed, 30 Sep 2026 · Ashwin NN, 2083 BS"), computed in `Asia/Kathmandu`
  via existing `src/lib/i18n/bsDate.ts`; Nepali month names when locale is `ne`. Renders
  AD only if the date is outside the converter's range.
- **`NotificationsButton`**: 40 px round icon button, `aria-label="Notifications"`, opens a
  Popover containing a `ComingSoonSlot` ("Notifications — shipment exceptions, manifest
  alerts and settlement approvals will appear here."). No unread dot (no data).

### 4.4 Surface wiring

- **Staff** (`components/layout/AppShell.tsx`): brand = organization name (skeleton while
  session loads, fallback `common.appName`), subtitle "Operations console"; scope =
  restyled `BranchSwitcher` (still `null` for <2 branches); `headerStart` = `HeaderSearch`;
  `headerEnd` = `LanguageSwitcher`, `ThemeToggle`, `UserMenu`; banners =
  `ImpersonationBanner` + `TrialStatusBanner`; `ReauthDialog` + `CommandPalette` stay.
  `NavItem` gains `group: "operations" | "finance" | "network" | "admin"`:
  - Operations: Dashboard, Shipments, Manifests, Riders, Live map, NDR, RTO, My deliveries
  - Finance: COD, Billing
  - Network: Branches
  - Admin: Admin (Users & roles), Audit log
  Group labels are next-intl keys (`nav.groups.*`, en + ne). `NAV_ITEMS` stays the single
  source for sidebar and command palette.
- **Merchant** (`MerchantAppShell`): brand = merchant display name, subtitle "Merchant
  portal"; `headerEnd` = `ThemeToggle`, `MerchantUserMenu`. Groups: Overview (Dashboard),
  Shipping (Shipments, Pickups), Money (COD & Invoices), Account (KYC, Staff, API keys).
- **Platform** (`app/platform/layout.tsx`): replaces `PlatformNav` with `ConsoleShell`;
  brand "Platform Console". Groups: Tenants (Dashboard, Organizations, Plans, Invoices),
  Reference (Countries), System (Jobs, Queues, Audit logs, Settings). Active match by
  prefix (current behaviour). Sign-out moves into a new `PlatformUserMenu` dropdown
  (same `POST /api/platform-auth/logout` → `/platform-login`). `PlatformNav.tsx` deleted.
  The platform login page stays outside the shell (unchanged).

Existing brand/nav components (`app-sidebar.tsx`, `nav-main.tsx`,
`MerchantAppSidebar.tsx`, `MerchantNavMain.tsx`) become thin adapters that build
`ConsoleNavGroup[]` for their surface, or are folded into their shell file; no duplicate
sidebar markup remains.

## 5. Page primitives (`src/web/src/components/page/`)

All presentational: plain props, no fetching, no state.

| Component | Props (summary) | Notes |
|---|---|---|
| `PageHeader` | `title`, `description?`, `badge?`, `actions?` | h1 Space Grotesk 26/600; stacks < `sm`. R1 migrates `DashboardHeader` to it. |
| `SectionCard` | `title`, `icon?`, `description?`, `action?`, `children` | Wraps `Card`; title Space Grotesk 16/600 with the existing `h-9 w-9 rounded-lg bg-muted` icon chip when `icon` given. |
| `KpiTile` | `label`, `value`, `delta?: { text, tone: "positive" \| "negative" \| "neutral" }`, `note?` | Delta always carries a text sign ("+1.2 pts"); tone colors from `--status-delivered`/`--status-ndr`/muted. `states/StatTile` re-exports a compatible wrapper. |
| `PipelineStrip` | `stages: { key, label, count, token, share?, caption?, href? }[]` | `token` is a `statusConfig` token name; renders `<a>` when `href`. Responsive: 7 → 4 → 2 columns. |
| `ComingSoonSlot` | `title`, `description`, `icon?`, `minHeight?` | Dashed border, `role="region"` + `aria-label=title`, fixed caption "Needs backend". Grep-able marker for later wiring. |
| `SheetHeaderBlock` | `eyebrow`, `title`, `mono?`, `status?`, `facts?: { label, value }[]` | Standard detail-Sheet header; adopted by Sheets in R2–R6. |

## 6. Restyled in place

- `DataTable` / `BorderedTableContainer`: container `bg-card border rounded-xl`; header row
  `bg-surface-sunken text-[11.5px] text-muted-foreground`; rows min 52 px, hairline
  dividers; new optional `selectedRowId` + `getRowId` props → selected row
  `bg-accent` + `aria-selected="true"`. Pagination unchanged.
- `StatusBadge`: same dot + label; `h-6 text-xs font-medium`, `rounded-md`.
- `EmptyState` and `LoadingState`: `bg-card` surface. `ErrorState` keeps its destructive tint (reads correctly on sand).
- Tabs: unchanged (already ruled-underline).

## 7. Error / loading behaviour

- Shell brand keeps its session-loading skeleton; nav shows skeleton rows while
  `navLoading`; session failure falls back to the product name (current behaviour).
- `ComingSoonSlot` never fetches or throws.
- Header date computation is pure; out-of-range BS dates degrade to AD only.

## 8. Testing (Vitest + Testing Library)

- `ConsoleShell`: renders brand/scope/header slots; drops an empty group; active item has
  `aria-current="page"`; collapsed items keep accessible names; navLoading → skeletons.
- Staff nav: rewrite `app-sidebar.test.tsx` / `nav-main.test.tsx` for groups; a user
  without finance/admin permissions sees neither heading; `CommandPalette` still lists every
  permitted page (guards the single-source rule).
- Merchant nav: groups + active state (update `MerchantNavMain.test.tsx`).
- Platform: groups render; prefix active match; `PlatformUserMenu` sign-out posts logout and
  redirects.
- Header: `DualDate` for a fixed instant renders the AD date and exactly the BS date that
  `bsDate.ts` returns for that Kathmandu calendar day (not a hand-computed literal);
  `NotificationsButton` opens a popover containing the "Needs backend" slot; breadcrumb
  shows group › page.
- Primitives: `KpiTile` delta text always present with tone; `PipelineStrip` renders links
  when `href`; `ComingSoonSlot` region + caption; `DataTable` `selectedRowId` sets
  `aria-selected`.
- Existing suite: update assertions tied to removed markup/classes only; no behavioural
  assertion may be weakened.

## 9. Documentation

- `DESIGN.md`: new surface tokens and rail; replace the flat-sidebar rule with "grouped,
  non-collapsible section headings"; add the **No-Fake-Numbers Rule** (missing data renders a
  `ComingSoonSlot`, never mock values); note `ConsoleShell` as the one shell for staff,
  merchant and platform; update the Cards "background = page" note.
- `docs/FRONTEND-ROADMAP.md`: add Phase F15 "Console Redesign" with R1–R6 and R1's backend
  follow-ups (notification feed, nav badge counts, plan usage in session payload).

## 10. Backend follow-ups surfaced by R1

1. Notification feed endpoint (header bell).
2. Nav badge counts (e.g. open NDRs) — lightweight counts endpoint.
3. Plan/branch usage in the session payload (sidebar plan card).

## 11. Delivery

Branch `feat/f15-r1-console-foundation` in a git worktree; subagent-driven execution per
the implementation plan; not merged until the user approves.
