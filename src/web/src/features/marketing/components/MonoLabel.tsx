import type { ReactNode } from "react";

/** Small uppercase Plex Mono label/stamp — reserved for AWBs, numerals, and eyebrows. */
export function MonoLabel({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <span
      className={`font-mono text-[0.6875rem] font-medium uppercase tracking-[0.14em] text-marketing-muted-fg ${className}`}
    >
      {children}
    </span>
  );
}
