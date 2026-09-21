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
