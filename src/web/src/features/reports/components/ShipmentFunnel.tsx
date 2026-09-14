"use client";

import { Bar, BarChart, CartesianGrid, Cell, XAxis, YAxis } from "recharts";
import { Package } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { EmptyState } from "@/components/states/EmptyState";
import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart";

const FUNNEL_BUCKETS: Array<{ label: string; token: string; statuses: string[] }> = [
  { label: "Created", token: "status-created", statuses: ["Created", "PickupAssigned"] },
  { label: "Picked Up", token: "status-in-transit", statuses: ["PickedUp"] },
  {
    label: "In Transit",
    token: "status-in-transit",
    statuses: ["AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch"],
  },
  { label: "Out for Delivery", token: "status-out-for-delivery", statuses: ["OutForDelivery"] },
  { label: "Delivered", token: "status-delivered", statuses: ["Delivered"] },
  { label: "NDR", token: "status-ndr", statuses: ["DeliveryFailed", "NdrCreated"] },
  {
    label: "RTO",
    token: "status-rto",
    statuses: ["RtoInitiated", "RtoInTransit", "RtoAtOrigin", "RtoDelivered"],
  },
  { label: "Cancelled", token: "status-cancelled", statuses: ["Cancelled"] },
];

const chartConfig = {
  count: { label: "Shipments" },
} satisfies ChartConfig;

export function ShipmentFunnel({ data }: { data: Record<string, number> }) {
  const chartData = FUNNEL_BUCKETS.map((bucket) => ({
    status: bucket.label,
    count: sumStatuses(data, bucket.statuses),
    fill: `hsl(var(--${bucket.token}))`,
  }));
  const hasActivity = chartData.some((bucket) => bucket.count > 0);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-3 font-display text-base">
          <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-muted">
            <Package className="h-4 w-4 text-muted-foreground" strokeWidth={2} />
          </span>
          Today&apos;s Shipments
        </CardTitle>
        <CardDescription>Shipment activity for today</CardDescription>
      </CardHeader>
      <CardContent>
        {hasActivity ? (
          <ChartContainer config={chartConfig} className="aspect-auto h-[300px] w-full">
            <BarChart accessibilityLayer data={chartData} layout="vertical" margin={{ top: 8, right: 16, bottom: 8 }}>
              <CartesianGrid horizontal={false} />
              <XAxis dataKey="count" type="number" hide />
              <YAxis
                dataKey="status"
                type="category"
                tickLine={false}
                axisLine={false}
                interval={0}
                width={96}
                tickMargin={8}
              />
              <ChartTooltip cursor={{ fill: "hsl(var(--muted))" }} content={<ChartTooltipContent indicator="line" />} />
              <Bar dataKey="count" radius={[0, 4, 4, 0]}>
                {chartData.map((entry) => (
                  <Cell key={entry.status} fill={entry.fill} />
                ))}
              </Bar>
            </BarChart>
          </ChartContainer>
        ) : (
          <EmptyState
            title="No shipment activity yet"
            description="Today's shipments appear here once bookings start coming in."
            icon={Package}
          />
        )}
      </CardContent>
    </Card>
  );
}

function sumStatuses(data: Record<string, number>, statuses: string[]): number {
  return statuses.reduce((sum, status) => sum + (data[status] ?? 0), 0);
}
