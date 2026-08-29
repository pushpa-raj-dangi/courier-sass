import { Gauge } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

function formatPercent(value: number | null): string {
  return value === null ? "—" : `${(value * 100).toFixed(1)}%`;
}

// .NET TimeSpan's default JSON string, e.g. "02:15:30" or "1.02:15:30" (d.hh:mm:ss)
function formatDuration(value: string | null): string {
  if (value === null) return "—";
  const [dayPart, timePart] = value.includes(".") ? value.split(".") : [null, value];
  const [hh, mm] = timePart.split(":");
  const hours = (dayPart ? parseInt(dayPart, 10) * 24 : 0) + parseInt(hh, 10);
  return `${hours}h ${parseInt(mm, 10)}m`;
}

export function RateTiles({
  data,
}: {
  data: {
    overallDeliverySuccessRate: number | null;
    overallNdrRate: number | null;
    overallRtoRate: number | null;
    averageDeliveryTime: string | null;
  };
}) {
  const tiles = [
    { label: "Delivery Success Rate", value: formatPercent(data.overallDeliverySuccessRate) },
    { label: "NDR Rate", value: formatPercent(data.overallNdrRate) },
    { label: "RTO Rate", value: formatPercent(data.overallRtoRate) },
    { label: "Average Delivery Time", value: formatDuration(data.averageDeliveryTime) },
  ];
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 font-display text-base">
          <Gauge className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          Key Rates
        </CardTitle>
        <CardDescription>Last 30 days</CardDescription>
      </CardHeader>
      <CardContent className="grid grid-cols-2 gap-6 sm:grid-cols-4">
        {tiles.map((tile) => (
          <div key={tile.label} className="flex flex-col gap-1">
            <span className="text-xs text-muted-foreground">{tile.label}</span>
            <span className="font-sans text-2xl font-semibold text-foreground">{tile.value}</span>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
