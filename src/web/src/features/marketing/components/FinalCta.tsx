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
