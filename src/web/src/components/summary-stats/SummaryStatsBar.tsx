import type { LucideIcon } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

export interface StatTile {
  label: string;
  value: string;
  icon: LucideIcon;
  badgeClass: string;
  highlight?: boolean;
}

export function SummaryStatsBar({
  tiles,
  isLoading,
  isError,
  skeletonCount = 4,
}: {
  tiles: StatTile[];
  isLoading: boolean;
  isError: boolean;
  skeletonCount?: number;
}) {
  if (isError) {
    return (
      <p data-testid="summary-stats-error" className="text-sm text-muted-foreground">
        Summary unavailable right now.
      </p>
    );
  }

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {Array.from({ length: skeletonCount }).map((_, i) => (
          <Skeleton key={i} data-testid="summary-stat-skeleton" className="h-[68px] w-full rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
      {tiles.map((tile) => {
        const Icon = tile.icon;
        return (
          <Card key={tile.label}>
            <CardContent className="flex items-center gap-3 py-4">
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-lg ${tile.badgeClass}`}>
                <Icon className="h-5 w-5" strokeWidth={2} />
              </span>
              <div className="flex flex-col gap-0.5 overflow-hidden">
                <span className="truncate text-xs text-muted-foreground">{tile.label}</span>
                <span
                  className={`truncate font-sans text-lg font-semibold tabular-nums sm:text-xl ${
                    tile.highlight ? "text-primary" : "text-foreground"
                  }`}
                >
                  {tile.value}
                </span>
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}
