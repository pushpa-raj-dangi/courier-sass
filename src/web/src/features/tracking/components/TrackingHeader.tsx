import Link from "next/link";
import { Package } from "lucide-react";
import { MarketingThemeToggle } from "@/features/marketing/components/MarketingThemeToggle";

export function TrackingHeader({ showBack = false }: { showBack?: boolean }) {
  return (
    <header className="flex h-14 items-center justify-between border-b border-border bg-background px-4">
      <Link href="/" className="flex min-h-11 items-center gap-2 font-display text-sm font-semibold text-foreground">
        <span className="flex size-7 items-center justify-center rounded-md bg-primary text-primary-foreground" aria-hidden="true">
          <Package className="size-4" />
        </span>
        Courier Platform
      </Link>
      <div className="flex items-center gap-2">
        {showBack && (
          <Link
            href="/track"
            className="flex min-h-11 items-center px-2 text-sm text-muted-foreground hover:text-foreground"
          >
            Track another parcel
          </Link>
        )}
        <MarketingThemeToggle />
      </div>
    </header>
  );
}
