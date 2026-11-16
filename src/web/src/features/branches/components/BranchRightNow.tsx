import Link from "next/link";
import { ErrorState } from "@/components/states/ErrorState";
import { onHandShipmentsHref, outForDeliveryHref, ridersOutHref } from "@/features/branches/lib/branchModel";
import type { BranchActivityRow } from "@/features/branches/types";

const CARDS: { label: string; pick: (r: BranchActivityRow) => number; href: (id: string) => string; linkText: string }[] = [
  { label: "On hand", pick: (r) => r.onHand, href: onHandShipmentsHref, linkText: "View shipments" },
  { label: "Out for delivery", pick: (r) => r.outForDelivery, href: outForDeliveryHref, linkText: "View shipments" },
  { label: "Riders out", pick: (r) => r.ridersOut, href: ridersOutHref, linkText: "View riders" },
  { label: "Open exceptions", pick: (r) => r.openExceptions, href: () => "/ndrs", linkText: "View NDR queue" },
];

/** `row` undefined = activity not loaded (values "—"); a loaded branch with no row passes zeroActivity. */
export function BranchRightNow({ branchId, row, isError, onRetry }: { branchId: string; row?: BranchActivityRow; isError: boolean; onRetry: () => void }) {
  return (
    <section aria-label="Right now" className="space-y-2">
      <h3 className="text-sm font-semibold text-foreground">Right now</h3>
      {isError ? (
        <ErrorState title="Couldn't load live counts" description="This branch's live counts are unavailable right now." onRetry={onRetry} />
      ) : (
        <div className="grid grid-cols-2 gap-2.5">
          {CARDS.map((card) => (
            <Link
              key={card.label}
              href={card.href(branchId)}
              className="flex flex-col gap-1 rounded-lg border border-border bg-background px-3.5 py-3 transition-colors hover:bg-accent"
            >
              <span className="text-xs text-muted-foreground">{card.label}</span>
              <span className="text-2xl font-semibold tabular-nums tracking-tight text-foreground">
                {row ? card.pick(row).toLocaleString("en-US") : "—"}
              </span>
              <span className="text-xs font-medium text-primary">{card.linkText}</span>
            </Link>
          ))}
        </div>
      )}
    </section>
  );
}
