"use client";

import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts";
import { Bike } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/states/EmptyState";
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart";
import { useRiders } from "@/features/riders/hooks/useRiders";
import type { RiderPerformance } from "@/features/reports/types";

function formatPercent(value: number | null): string {
  return value === null ? "—" : `${(value * 100).toFixed(1)}%`;
}

const chartConfig = {
  attempts: { label: "Delivery attempts", color: "hsl(var(--primary))" },
} satisfies ChartConfig;

export function RiderPerformanceChart({ data }: { data: RiderPerformance[] }) {
  const { data: riders } = useRiders();
  const riderLabel = (id: string) => riders?.find((r) => r.id === id)?.licenseNumber ?? "Unknown rider";

  const rows = data
    .map((perf) => ({ riderId: perf.riderId, label: riderLabel(perf.riderId), perf }))
    .sort((a, b) => b.perf.attempts - a.perf.attempts);

  const chartData = rows.map((row) => ({ rider: row.label, attempts: row.perf.attempts }));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Bike className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Rider Performance
        </CardTitle>
        <CardDescription>Delivery attempts by rider</CardDescription>
      </CardHeader>
      <CardContent>
        {rows.length === 0 ? (
          <EmptyState title="No rider activity yet" description="Rider performance appears once deliveries are attempted." icon={Bike} />
        ) : (
          <div className="space-y-6">
            <ChartContainer config={chartConfig} className="aspect-auto h-[300px] w-full">
              <BarChart accessibilityLayer data={chartData} margin={{ top: 8, bottom: 24 }}>
                <CartesianGrid vertical={false} />
                <XAxis
                  dataKey="rider"
                  tickLine={false}
                  axisLine={false}
                  interval={0}
                  angle={-35}
                  textAnchor="end"
                  height={56}
                  tickMargin={8}
                />
                <YAxis dataKey="attempts" type="number" hide />
                <ChartTooltip cursor={{ fill: "hsl(var(--muted))" }} content={<ChartTooltipContent indicator="line" />} />
                <Bar dataKey="attempts" fill="var(--color-attempts)" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ChartContainer>
            <div className="space-y-2 border-t border-border pt-4">
              {rows.map((row) => (
                <div key={row.riderId} className="flex items-center justify-between text-xs text-muted-foreground">
                  <span className="truncate">{row.label}</span>
                  <span className="tabular-nums">
                    {row.perf.successes}/{row.perf.attempts} delivered &middot; {formatPercent(row.perf.successRate)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
