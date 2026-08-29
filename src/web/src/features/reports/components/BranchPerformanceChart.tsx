"use client";

import { Bar, BarChart, CartesianGrid, XAxis, YAxis } from "recharts";
import { Building2 } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/states/EmptyState";
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart";
import { useBranches } from "@/features/branches/hooks/useBranches";
import type { BranchPerformance } from "@/features/reports/types";

function formatPercent(value: number | null): string {
  return value === null ? "—" : `${(value * 100).toFixed(1)}%`;
}

const chartConfig = {
  volume: { label: "Shipments", color: "hsl(var(--primary))" },
} satisfies ChartConfig;

export function BranchPerformanceChart({ data }: { data: Record<string, BranchPerformance> }) {
  const { data: branches } = useBranches();
  const branchName = (id: string) => branches?.find((b) => b.id === id)?.name ?? "Unknown branch";

  const rows = Object.entries(data)
    .map(([branchId, perf]) => ({ branchId, label: branchName(branchId), perf }))
    .sort((a, b) => b.perf.shipmentVolume - a.perf.shipmentVolume);

  const chartData = rows.map((row) => ({ branch: row.label, volume: row.perf.shipmentVolume }));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Building2 className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Branch Performance
        </CardTitle>
        <CardDescription>Shipment volume by branch</CardDescription>
      </CardHeader>
      <CardContent>
        {rows.length === 0 ? (
          <EmptyState title="No branch activity yet" description="Branch performance appears once shipments start moving." icon={Building2} />
        ) : (
          <div className="space-y-6">
            <ChartContainer
              config={chartConfig}
              className="aspect-auto w-full"
              style={{ height: Math.max(140, rows.length * 44) }}
            >
              <BarChart accessibilityLayer data={chartData} layout="vertical" margin={{ left: 0 }}>
                <CartesianGrid horizontal={false} />
                <YAxis
                  dataKey="branch"
                  type="category"
                  tickLine={false}
                  axisLine={false}
                  tickMargin={8}
                  width={130}
                  interval={0}
                />
                <XAxis dataKey="volume" type="number" hide />
                <ChartTooltip cursor={{ fill: "hsl(var(--muted))" }} content={<ChartTooltipContent indicator="line" />} />
                <Bar dataKey="volume" fill="var(--color-volume)" radius={4} />
              </BarChart>
            </ChartContainer>
            <div className="space-y-2 border-t border-border pt-4">
              {rows.map((row) => (
                <div key={row.branchId} className="flex items-center justify-between text-xs text-muted-foreground">
                  <span className="truncate">{row.label}</span>
                  <span className="tabular-nums">
                    Delivery {formatPercent(row.perf.deliverySuccessRate)} &middot; NDR{" "}
                    {formatPercent(row.perf.ndrRate)} &middot; RTO {formatPercent(row.perf.rtoRate)}
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
