import { Info } from "lucide-react";

/** Shown on merchant-level tabs while the page header has a branch selected, which they ignore. */
export function MerchantLevelNote() {
  return (
    <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
      <Info className="h-3.5 w-3.5" aria-hidden="true" />
      Merchant-level — not filtered by branch
    </p>
  );
}
