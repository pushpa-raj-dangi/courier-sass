import { TrendingUp } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { RadialMeter } from "@/components/charts/RadialMeter";

export function DeliveryRateHero({ rate }: { rate: number | null }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <TrendingUp className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Delivery Success Rate
        </CardTitle>
        <CardDescription>Share of shipments delivered successfully</CardDescription>
      </CardHeader>
      <CardContent className="flex items-center justify-center py-4">
        <RadialMeter value={rate} colorToken="status-delivered" label="Delivered" />
      </CardContent>
    </Card>
  );
}
