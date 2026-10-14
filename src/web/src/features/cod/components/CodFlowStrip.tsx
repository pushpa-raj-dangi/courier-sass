"use client";

import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { cn } from "@/lib/utils";
import { FLOW_LABELS, STAGE_TAB, pickBottleneck } from "@/features/cod/lib/flowModel";
import { formatRs } from "@/features/cod/lib/codFormat";
import type { CodTab } from "@/features/cod/lib/codTabs";
import type { CodFlow } from "@/features/cod/types";

const parcels = new Intl.NumberFormat("en-IN");

export function CodFlowStrip({
  flow,
  isLoading,
  isError,
  onRetry,
  onSelectTab,
}: {
  flow: CodFlow | undefined;
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  onSelectTab: (tab: CodTab) => void;
}) {
  if (isLoading) {
    return (
      <div role="status" aria-label="Loading COD lifecycle" className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-6">
        {Array.from({ length: 6 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }
  if (isError || !flow) {
    return <ErrorState title="Couldn't load the COD flow" description="Money totals are unavailable right now." onRetry={onRetry} />;
  }

  const bottleneck = pickBottleneck(flow.stages);
  return (
    <ol aria-label="COD lifecycle" className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-6">
      {flow.stages.map((stage, i) => {
        const active = stage.key === bottleneck;
        const muted = active ? "text-primary-foreground/80" : "text-muted-foreground";
        return (
          <li key={stage.key}>
            <button
              type="button"
              onClick={() => onSelectTab(STAGE_TAB[stage.key])}
              aria-current={active ? "step" : undefined}
              className={cn(
                "flex h-full w-full flex-col gap-1 rounded-lg border px-4 py-3 text-left transition-colors",
                active ? "border-primary bg-primary text-primary-foreground" : "border-border bg-card text-foreground hover:bg-accent",
              )}
            >
              <span className={cn("font-mono text-xs tracking-widest", muted)}>{String(i + 1).padStart(2, "0")}</span>
              <span className={cn("text-xs font-medium", muted)}>{FLOW_LABELS[stage.key]}</span>
              <span className="text-lg font-semibold tabular-nums tracking-tight">{formatRs(stage.amount)}</span>
              <span className={cn("text-xs", muted)}>{parcels.format(stage.count)} parcels</span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}
