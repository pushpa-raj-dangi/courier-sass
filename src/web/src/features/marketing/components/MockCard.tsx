import type { ReactNode } from "react";
import { MonoLabel } from "./MonoLabel";

/** Thin bar with a sweeping scan line; the sweep is CSS-only and static under prefers-reduced-motion. */
export function ScanBar() {
  return (
    <div aria-hidden="true" className="h-0.5 w-full overflow-hidden rounded-sm bg-marketing-border/40">
      <div className="marketing-scan-line h-full w-1/4 bg-marketing-accent" />
    </div>
  );
}

/**
 * CSS-built UI card showing fictional sample data. The hard offset shadow (not blur) gives depth
 * in the editorial style; the "Sample data" tag keeps every mock honest (no fabricated evidence).
 */
export function MockCard({
  label,
  children,
  className = "",
}: {
  label: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`rounded-md border border-marketing-border bg-marketing-bg-elevated p-4 shadow-[6px_6px_0_0_hsl(var(--marketing-accent)/0.18)] motion-safe:transition-transform motion-safe:hover:-translate-y-0.5 ${className}`}
    >
      <div className="mb-3 flex items-center justify-between gap-3">
        <MonoLabel className="text-marketing-fg">{label}</MonoLabel>
        <MonoLabel>Sample data</MonoLabel>
      </div>
      {children}
    </div>
  );
}
