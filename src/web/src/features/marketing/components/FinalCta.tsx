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
