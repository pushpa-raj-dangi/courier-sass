import type { ReactNode } from "react";

export interface BorderedTableContainerProps {
  children: ReactNode;
}

// The one bordered table frame (DataTable and every raw Table). F15 R1: card surface on the sand
// page — cards and page no longer share a color, so bg-card now reads as a lifted panel.
export function BorderedTableContainer({ children }: BorderedTableContainerProps) {
  return <div className="overflow-hidden rounded-xl border border-border bg-card">{children}</div>;
}
