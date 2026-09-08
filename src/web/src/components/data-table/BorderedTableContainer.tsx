import type { ReactNode } from "react";

export interface BorderedTableContainerProps {
  children: ReactNode;
}

// Shared wrapper for a raw `Table` used outside `DataTable` (non-paginated merchant-scoped
// lists). `bg-muted/40` rather than `bg-card` is deliberate: these lists sit directly on the
// page background, and `bg-card` is hex-identical to it in light mode.
export function BorderedTableContainer({ children }: BorderedTableContainerProps) {
  return (
    <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
      {children}
    </div>
  );
}
