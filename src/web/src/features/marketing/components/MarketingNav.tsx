"use client";

import Link from "next/link";
import { Menu, Package } from "lucide-react";
import { NAV_LINKS, getPrimaryCta } from "../content";
import { Sheet, SheetClose, SheetContent, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { MarketingThemeToggle } from "./MarketingThemeToggle";

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
        <div className="flex items-center gap-2">
          <Link
            href={cta.href}
            className="rounded-lg bg-marketing-accent px-4 py-2 text-sm font-medium text-marketing-accent-fg transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg"
          >
            {cta.label}
          </Link>
          <MarketingThemeToggle />
          <Sheet>
            <SheetTrigger asChild>
              <button
                type="button"
                aria-label="Open menu"
                className="flex h-9 w-9 items-center justify-center rounded-md text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg md:hidden"
              >
                <Menu className="h-5 w-5" strokeWidth={2} aria-hidden="true" />
              </button>
            </SheetTrigger>
            <SheetContent
              side="right"
              className="w-3/4 border-marketing-border bg-marketing-bg text-marketing-fg sm:max-w-xs"
            >
              <SheetTitle className="font-display text-base font-semibold text-marketing-fg">Menu</SheetTitle>
              <div className="mt-6 flex flex-col gap-1">
                {NAV_LINKS.map((link) => (
                  <SheetClose asChild key={link.href}>
                    <Link
                      href={link.href}
                      className="rounded-md px-2 py-3 text-base text-marketing-fg transition-colors hover:bg-marketing-bg-elevated focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent"
                    >
                      {link.label}
                    </Link>
                  </SheetClose>
                ))}
              </div>
            </SheetContent>
          </Sheet>
        </div>
      </nav>
    </header>
  );
}
