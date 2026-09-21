import type { ReactNode } from "react";
import Link from "next/link";
import type { LucideIcon } from "lucide-react";
import { AuthLedgerShowcase } from "@/components/auth/AuthLedgerShowcase";

interface AuthPageShellProps {
  icon: LucideIcon;
  brand: string;
  headline: string;
  description: string;
  /** The cross-link in the top corner, e.g. "Already have an account?" + "Sign in". */
  switchPrompt: string;
  switchLabel: string;
  switchHref: string;
  children: ReactNode;
}

/**
 * The one frame for staff `/login` and `/register`, so the two pages share their grid ratio,
 * gutters, top bar, and form column exactly: only the headline copy and the form differ.
 * Below `lg` the showcase is hidden and an inline brand bar takes its place.
 */
export function AuthPageShell({
  icon: Icon,
  brand,
  headline,
  description,
  switchPrompt,
  switchLabel,
  switchHref,
  children,
}: AuthPageShellProps) {
  return (
    <main className="grid min-h-screen lg:grid-cols-[2fr_3fr]">
      <AuthLedgerShowcase icon={Icon} brand={brand} headline={headline} description={description} />
      <div className="flex min-h-screen flex-col bg-background">
        <div className="flex items-center justify-between px-4 py-3 sm:px-8 lg:justify-end lg:px-10">
          <div className="flex items-center gap-2.5 lg:hidden">
            <span className="flex h-7 w-7 items-center justify-center rounded-md bg-primary">
              <Icon className="h-3.5 w-3.5 text-primary-foreground" strokeWidth={2} />
            </span>
            <span className="font-display text-base font-semibold tracking-tight">{brand}</span>
          </div>
          <p className="text-sm text-muted-foreground">
            {switchPrompt}{" "}
            <Link
              href={switchHref}
              className="rounded-sm font-medium text-foreground underline underline-offset-4 hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
            >
              {switchLabel}
            </Link>
          </p>
        </div>
        <div className="flex flex-1 items-center justify-center px-4 pb-8 pt-2 sm:px-8 lg:px-10">{children}</div>
      </div>
    </main>
  );
}
