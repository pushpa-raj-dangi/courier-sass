import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";

export interface BarRow {
  key: string;
  label: string;
  value: number;
  color: string;
}

// A row-per-category horizontal bar chart (dataviz skill: "compare magnitude across
// ordinal/status categories"). Bars share one baseline (left edge, square) with a
// 4px-rounded tip; each is its own hover/focus hit target with a tooltip, and the
// count is always direct-labeled so no value is tooltip-only.
export function HorizontalBarList({
  rows,
  maxValue,
  valueFormatter = (value) => value.toLocaleString(),
}: {
  rows: BarRow[];
  maxValue?: number;
  valueFormatter?: (value: number) => string;
}) {
  const max = maxValue ?? Math.max(1, ...rows.map((row) => row.value));

  return (
    <TooltipProvider delayDuration={200}>
      <div className="space-y-3">
        {rows.map((row) => {
          const widthPercent = max === 0 ? 0 : Math.min(100, (row.value / max) * 100);
          return (
            <Tooltip key={row.key}>
              <TooltipTrigger asChild>
                <div
                  tabIndex={0}
                  className="grid grid-cols-[7rem_1fr_3rem] items-center gap-3 rounded-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring sm:grid-cols-[9rem_1fr_3rem]"
                >
                  <span className="flex items-center gap-1.5 truncate text-xs text-muted-foreground">
                    <span
                      className="h-2 w-2 shrink-0 rounded-full"
                      style={{ backgroundColor: row.color }}
                      aria-hidden="true"
                    />
                    <span className="truncate">{row.label}</span>
                  </span>
                  <span className="h-3 rounded-sm bg-muted">
                    <span
                      data-testid="bar-fill"
                      className="block h-3 rounded-r-[4px] transition-[width]"
                      style={{ width: `${widthPercent}%`, backgroundColor: row.color }}
                    />
                  </span>
                  <span className="text-right text-sm font-medium tabular-nums text-foreground">
                    {valueFormatter(row.value)}
                  </span>
                </div>
              </TooltipTrigger>
              <TooltipContent side="top">
                {row.label}: {valueFormatter(row.value)}
              </TooltipContent>
            </Tooltip>
          );
        })}
      </div>
    </TooltipProvider>
  );
}
