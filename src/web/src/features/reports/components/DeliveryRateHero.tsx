"use client";

import { Label, PolarAngleAxis, PolarRadiusAxis, RadialBar, RadialBarChart } from "recharts";
import { TrendingUp } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart";

const chartConfig = {
  rate: { label: "Delivered", color: "hsl(var(--status-delivered))" },
} satisfies ChartConfig;

export function DeliveryRateHero({ rate }: { rate: number | null }) {
  const percent = rate === null ? 0 : Math.round(Math.min(1, Math.max(0, rate)) * 100);
  const chartData = [{ metric: "rate", value: percent, fill: "var(--color-rate)" }];

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
        <ChartContainer config={chartConfig} className="mx-auto aspect-square w-full max-h-[220px]">
          <RadialBarChart
            data={chartData}
            startAngle={90}
            endAngle={-270}
            innerRadius={80}
            outerRadius={110}
          >
            <PolarAngleAxis type="number" dataKey="value" domain={[0, 100]} tick={false} axisLine={false} />
            <ChartTooltip cursor={false} content={<ChartTooltipContent hideLabel nameKey="metric" />} />
            <RadialBar dataKey="value" background={{ fill: "hsl(var(--status-delivered) / 0.15)" }} cornerRadius={10} />
            <PolarRadiusAxis tick={false} tickLine={false} axisLine={false}>
              <Label
                content={({ viewBox }) => {
                  if (viewBox && "cx" in viewBox && "cy" in viewBox) {
                    return (
                      <text x={viewBox.cx} y={viewBox.cy} textAnchor="middle">
                        <tspan x={viewBox.cx} y={viewBox.cy} className="fill-foreground text-3xl font-semibold">
                          {rate === null ? "—" : `${percent}%`}
                        </tspan>
                        <tspan x={viewBox.cx} y={(viewBox.cy ?? 0) + 22} className="fill-muted-foreground text-sm">
                          Delivered
                        </tspan>
                      </text>
                    );
                  }
                  return null;
                }}
              />
            </PolarRadiusAxis>
          </RadialBarChart>
        </ChartContainer>
      </CardContent>
    </Card>
  );
}
