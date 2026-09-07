"use client";

import Link from "next/link";
import { Package } from "lucide-react";
import { NAV_LINKS, getPrimaryCta } from "../content";

export function MarketingNav({ isAuthenticated }: { isAuthenticated: boolean }) {
  const cta = getPrimaryCta(isAuthenticated);

  return (
    <header className="sticky top-0 z-40 border-b border-marketing-border/60 bg-marketing-bg/90 backdrop-blur">
      <nav className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4">
        <Link
          href="/"
          className="flex items-center gap-2 rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
        >
          <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-marketing-accent">
            <Package className="h-4 w-4 text-marketing-accent-fg" strokeWidth={2} />
          </span>
          <span className="font-display text-base font-semibold tracking-tight text-marketing-fg">
            Courier Platform
          </span>
        </Link>
        <div className="hidden items-center gap-6 md:flex">
          {NAV_LINKS.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className="rounded-md text-sm text-marketing-muted-fg transition-colors hover:text-marketing-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
            >
              {link.label}
            </Link>
          ))}
        </div>
        <Link
          href={cta.href}
          className="rounded-lg bg-marketing-accent px-4 py-2 text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
        >
          {cta.label}
        </Link>
      </nav>
    </header>
  );
}
