import type { LucideIcon } from "lucide-react";
import { KeyRound, ScrollText, ShieldCheck } from "lucide-react";
import { TONE_COLOR, type AuthLifecycleTone } from "@/components/auth/AuthLifecycleGraphic";

interface ScanEvent {
  time: string;
  event: string;
  detail: string;
  tone: AuthLifecycleTone;
}

// Illustrative data only — one parcel's scan history, labelled as a sample in the panel.
const SCANS: ScanEvent[] = [
  { time: "09:12", event: "Booked at counter", detail: "Kathmandu Hub · Counter staff", tone: "neutral" },
  { time: "11:40", event: "Dispatched on manifest", detail: "Kathmandu Hub → Pokhara Hub", tone: "motion" },
  { time: "06:05", event: "Received and scanned", detail: "Pokhara Hub · Sorting staff", tone: "motion" },
  { time: "10:22", event: "Out for delivery", detail: "Rider assigned · attempt 1", tone: "highlight" },
  { time: "13:48", event: "Delivered", detail: "OTP and photo proof · COD collected", tone: "success" },
];

const TRUST: { icon: LucideIcon; label: string }[] = [
  { icon: ShieldCheck, label: "Your data stays in your organization" },
  { icon: KeyRound, label: "Permission-based access per branch" },
  { icon: ScrollText, label: "Every change written to an audit trail" },
];

/**
 * Left-hand panel for `/login` and `/register`. Rather than only stating the promise, it shows
 * the mechanism behind it: a single parcel's custody ledger from
 * counter to delivery, ending in the COD arithmetic that settles with the merchant. One
 * hairline-bordered surface (no nested cards) on the fixed `auth-panel` navy. Hidden below
 * `lg`, where the form carries the task.
 */
export function AuthLedgerShowcase({
  icon: Icon,
  brand,
  headline,
  description,
}: {
  icon: LucideIcon;
  brand: string;
  headline: string;
  description: string;
}) {
  return (
    <div className="hidden bg-auth-panel text-auth-panel-foreground lg:block">
      {/* Sticky inner column: the grid cell stretches to the form's height (so the navy fills the
          whole side), while the content stays one viewport tall and pinned instead of spreading
          apart when the form scrolls. */}
      <div className="sticky top-0 flex min-h-screen flex-col justify-between gap-8 p-8 xl:p-10">
      <div className="flex items-center gap-2.5">
        <span className="flex h-8 w-8 items-center justify-center rounded-md bg-auth-panel-foreground/15">
          <Icon className="h-4 w-4" strokeWidth={2} />
        </span>
        <span className="font-display text-base font-semibold tracking-tight">{brand}</span>
      </div>

      <div className="flex max-w-lg flex-col gap-6">
        <div>
          {/* h2: the form's own title is the page's h1 and renders at every breakpoint. */}
          <h2 className="font-display text-3xl font-semibold leading-[1.15] tracking-tight text-balance">
            {headline}
          </h2>
          <p className="mt-3 max-w-md text-sm leading-relaxed text-auth-panel-foreground/75">{description}</p>
        </div>

        <figure
          className="rounded-lg border border-auth-panel-foreground/20 bg-auth-panel-foreground/[0.04]"
          aria-label="Sample shipment custody ledger"
        >
          <figcaption className="flex items-baseline justify-between gap-4 border-b border-auth-panel-foreground/15 px-4 py-2.5">
            <span className="font-mono text-sm tabular-nums">NP2026-004182</span>
            <span className="text-xs text-auth-panel-foreground/70">Sample shipment · illustrative</span>
          </figcaption>

          <ol className="relative px-4 py-3">
            <span
              aria-hidden="true"
              className="ledger-thread absolute bottom-6 left-[calc(1rem+3rem+0.75rem+0.375rem)] top-6 w-px bg-auth-panel-foreground/30"
            />
            {SCANS.map((scan, i) => (
              <li
                key={scan.event}
                style={{ "--i": i } as React.CSSProperties}
                className="ledger-row relative grid grid-cols-[3rem_0.75rem_1fr] items-start gap-x-3 py-1.5"
              >
                <span className="pt-0.5 font-mono text-xs tabular-nums text-auth-panel-foreground/70">
                  {scan.time}
                </span>
                <span
                  aria-hidden="true"
                  className="mt-1 h-3 w-3 rounded-full border-2 border-auth-panel"
                  style={{ backgroundColor: TONE_COLOR[scan.tone], boxShadow: "0 0 0 1px hsl(var(--auth-panel-foreground) / 0.4)" }}
                />
                <span>
                  <span className="block text-sm font-medium leading-snug">{scan.event}</span>
                  <span className="block text-xs leading-snug text-auth-panel-foreground/70">
                    {scan.detail}
                  </span>
                </span>
              </li>
            ))}
          </ol>

          <dl className="grid grid-cols-3 divide-x divide-auth-panel-foreground/15 border-t border-auth-panel-foreground/15 text-sm tabular-nums">
            <div className="px-4 py-2.5">
              <dt className="text-xs text-auth-panel-foreground/70">COD collected</dt>
              <dd className="mt-0.5 font-mono font-medium">NPR 2,500</dd>
            </div>
            <div className="px-4 py-2.5">
              <dt className="text-xs text-auth-panel-foreground/70">Courier fee</dt>
              <dd className="mt-0.5 font-mono font-medium">− NPR 150</dd>
            </div>
            <div className="px-4 py-2.5">
              <dt className="text-xs text-auth-panel-foreground/70">Merchant payable</dt>
              <dd className="mt-0.5 font-mono font-semibold">NPR 2,350</dd>
            </div>
          </dl>
        </figure>
      </div>

      <ul className="flex flex-col gap-2 text-xs text-auth-panel-foreground/80 xl:flex-row xl:gap-5">
        {TRUST.map(({ icon: TrustIcon, label }) => (
          <li key={label} className="flex items-center gap-2">
            <TrustIcon className="h-4 w-4 shrink-0 text-auth-panel-foreground/70" strokeWidth={2} />
            {label}
          </li>
        ))}
      </ul>
      </div>
    </div>
  );
}
