# F15 R5a — Public tracking redesign (short design)

Date: 2026-11-17. Source artboard: "Public tracking (mobile)" (`project/Track.dc.html`) on the
F15 design canvas. R5 was split: R5a = public `/track` (this doc), R5b = merchant portal (artboards
first, separate cycle).

## Intent

`/track` is used by recipients waiting for a parcel. Bring it in line with the F15 redesign
(artboard look) while showing only facts the backend can back up and nothing CLAUDE.md §24
forbids (staff, internal notes, financial info, sensitive customer info).

## Approaches considered

- **A (picked):** split R5 into R5a tracking + R5b merchant portal — small reviewable cycles,
  tracking ships fast because its artboard already exists.
- B: one R5 for everything — one big branch.
- C: restyle merchant pages without artboards — no design review.

## Scope decided with the user

| Artboard item | Decision |
|---|---|
| Destination ("To Jawalakhel, Lalitpur") | **Add** — area name only, never the street address |
| Sender ("from Everest Organics") | **Add** — merchant display name |
| "Arriving today by 6 PM" | Show **"Arriving today"** only while `OutForDelivery`; no time (no ETA exists) |
| "You are stop 15" | Drop |
| OTP hint / masked phone / COD exact change | Drop (SMS OTP not sent yet; COD is financial info) |
| "Change delivery time" / "Contact support" | Drop (nothing backs them) |

Kept as drawn: AWB search box, status card with headline, journey timeline (done / now / next),
"updated HH:MM" footer. Mobile-first, centred column on desktop.

## Backend (additive, read-only)

`PublicTrackingResponse` gains two trailing nullable fields:

- `DestinationArea`: `"{LocalLevel.Name}, {District.Name}"` from `RecipientLocalLevelId` /
  `RecipientDistrictId`; district name alone if only the district is set; else `RecipientCity`
  (international inbound); else `null`. Geography is global reference data.
- `SenderName`: the shipment's `Merchant.DisplayName` (`IgnoreQueryFilters`, by `MerchantId`) —
  never `Shipment.SenderName` (that can be a private person's name).

No new endpoint, no migration, existing anonymous + IP rate-limited route. The fail-closed duplicate
AWB rule stays.

Tests (Api.Tests `PublicTrackingEndpointTests`): both fields populated for a domestic shipment;
null-safe when geography is missing; response JSON contains no recipient address line, phone,
recipient/sender person names, COD/charges, or staff/rider ids.

## Frontend

- `/track/[awb]`: rebuilt per artboard — search box (reuse `TrackingSearchForm`/schema) at top;
  status card (status dot + label, display headline, "To {area} · from {sender}" line with each part
  omitted when null); journey of the six public stages Received → PickedUp → InTransit →
  AtDestinationBranch → OutForDelivery → Delivered with done/now/next states, each done step showing
  its event time and location. `DeliveryAttemptUnsuccessful` renders as the current step in the alert
  tone with the existing summary copy; `Cancelled` shows a cancelled card and no "next" steps.
  Footer: "updated {time}" from the query's last fetch time.
- `/track` landing restyled to the same look (search box + short helper text).
- Loading (skeleton), not found, rate limited and generic error states kept.
- Types: `destinationArea` and `senderName` (`string | null`) added to `PublicTrackingResult`.

## Builder's calls (Claude's, correctable)

- Visual tokens: use the console/app design tokens the artboard is drawn in (not the marketing
  palette the page uses today); keep the brand header link to `/` and the theme toggle.
- Headline copy per status lives in `features/tracking/lib/statusMeta.ts` next to the existing
  summaries; "Arriving today" is the `OutForDelivery` headline.
- Dates shown in the viewer's local time, en-GB style, no ICU month text assumptions in tests.

## Non-goals

ETA, stop number, OTP, COD, reschedule, support contact, merchant portal (R5b).

## Changes after final review

- New public status `ReturningToSender` (appended to `PublicTrackingStatus`, serialized by name).
  `RtoInitiated`, `RtoInTransit`, `RtoAtOrigin` and `RtoDelivered` now map to it instead of
  `DeliveryAttemptUnsuccessful`, so a returned parcel no longer reads "We missed you" with an
  upcoming "Delivered" step. `DeliveryFailed`/`NdrCreated` still map to
  `DeliveryAttemptUnsuccessful`. Web: label "Returning to Sender" (`status-rto`), headline
  "Returning to the seller"; the journey shows only reached stages, all done, with no now/next
  (same as `Cancelled`). The journey section is omitted when it would have no steps.
- The status summary sentence is rendered under the headline only for
  `DeliveryAttemptUnsuccessful` ("A delivery attempt was unsuccessful. We'll contact you or try
  again.") and `ReturningToSender` ("This parcel is on its way back to the seller.").
- "Arriving today" is shown only when the latest `OutForDelivery` event falls on the viewer's local
  today; otherwise the headline is "Out for delivery" (`headlineFor(result, now)` in
  `statusMeta.ts`).
- Smaller fixes: status label in `text-foreground` (the coloured dot carries the status colour) for
  contrast; app `ThemeToggle` in the header at a 44px target; icon-only "Track another parcel" link
  below `sm`; error states render their message as the page h1; the landing eyebrow is
  `aria-hidden`; the search box gets a destructive border when invalid; the merchant name lookup
  also matches the shipment's organization.
