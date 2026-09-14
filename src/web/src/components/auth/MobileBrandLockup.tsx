import type { LucideIcon } from "lucide-react";

// The below-`lg` counterpart to AuthShowcasePanel's brand lockup, shown once the showcase
// panel itself is hidden — same icon/brand props, repeated across every auth surface.
export function MobileBrandLockup({ icon: Icon, brand }: { icon: LucideIcon; brand: string }) {
  return (
    <>
      <span className="flex h-11 w-11 items-center justify-center rounded-lg bg-primary">
        <Icon className="h-5 w-5 text-primary-foreground" strokeWidth={2} />
      </span>
      <span className="font-display text-lg font-semibold tracking-tight">{brand}</span>
    </>
  );
}
