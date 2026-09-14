import { Gauge, TrendingDown, Undo2, Clock } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

function formatPercent(value: number | null): string {
  return value === null ? "—" : `${(value * 100).toFixed(1)}%`;
}

// .NET TimeSpan's default JSON string: "[d.]hh:mm:ss[.fffffff]" — the day separator and the
// fractional-seconds separator are both ".", so a naive first-"." split misparses a day-less
// value that has fractional seconds (e.g. "00:00:02.8836000") by treating "8836000" as hours.
function formatDuration(value: string | null): string {
  if (value === null) return "—";
  const match = value.match(/^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?$/);
  if (!match) return "—";
  const [, dayPart, hh, mm] = match;
  const hours = (dayPart ? parseInt(dayPart, 10) * 24 : 0) + parseInt(hh, 10);
  return `${hours}h ${parseInt(mm, 10)}m`;
}

export function RateTiles({
  data,
  rangeLabel = "Last 30 days",
}: {
  data: {
    overallNdrRate: number | null;
    overallRtoRate: number | null;
    averageDeliveryTime: string | null;
  };
  rangeLabel?: string;
}) {
  const tiles = [
    {
      label: "NDR Rate",
      value: formatPercent(data.overallNdrRate),
      icon: TrendingDown,
      badgeClass: "bg-status-ndr/15 text-status-ndr",
    },
    {
      label: "RTO Rate",
      value: formatPercent(data.overallRtoRate),
      icon: Undo2,
      badgeClass: "bg-status-rto/15 text-status-rto",
    },
    {
      label: "Average Delivery Time",
      value: formatDuration(data.averageDeliveryTime),
      icon: Clock,
      badgeClass: "bg-muted text-muted-foreground",
    },
  ];
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Gauge className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Key Rates
        </CardTitle>
        <CardDescription>{rangeLabel}</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-1 gap-4">
        {tiles.map((tile) => {
          const Icon = tile.icon;
          return (
            <div
              key={tile.label}
              className="flex items-center gap-3 rounded-lg border border-border p-4"
            >
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-lg ${tile.badgeClass}`}>
                <Icon className="h-5 w-5" strokeWidth={2} />
              </span>
              <div className="flex flex-col gap-0.5">
                <span className="text-xs text-muted-foreground">{tile.label}</span>
                <span className="font-sans text-lg font-semibold tabular-nums text-foreground">{tile.value}</span>
              </div>
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}
