import Link from "next/link";
import { FOOTER_LINKS } from "../content";

export function MarketingFooter() {
  return (
    <footer className="border-t border-marketing-border/60 px-4 py-10">
      <div className="mx-auto flex max-w-6xl flex-col items-center justify-between gap-4 sm:flex-row">
        <span className="font-display text-sm font-semibold text-marketing-fg">Courier Platform</span>
        <div className="flex flex-wrap items-center justify-center gap-6">
          {FOOTER_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="text-sm text-marketing-muted-fg transition-colors hover:text-marketing-fg"
            >
              {link.label}
            </Link>
          ))}
        </div>
      </div>
    </footer>
  );
}
