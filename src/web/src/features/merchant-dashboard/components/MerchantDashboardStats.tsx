"use client";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { useMerchantSettlements } from "@/features/merchant-cod/hooks/useMerchantSettlements";

const currencyFormatter = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function StatTile({ label, value }: { label: string; value: string }) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-xs font-medium text-muted-foreground">{label}</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="font-display text-2xl font-semibold text-foreground">{value}</p>
      </CardContent>
    </Card>
  );
}

// Small client-side aggregation over already-fetched list data (first 100 shipments, every
// settlement) — no new backend dashboard-aggregation endpoint, matching the design spec's
// resolved "no new backend read needed" decision for this screen.
export function MerchantDashboardStats() {
  const { data: shipments } = useMerchantShipments(1, 100);
  const { data: settlements } = useMerchantSettlements();

  const items = shipments?.items ?? [];
  // PickupAssigned is grouped with Created here to match statusConfig.ts's STATUS_CONFIG, which
  // gives PickupAssigned the same "Created" label/token as Created everywhere else in the app
  // (StatusBadge etc.) — without this, a shipment sitting in that normal transitional status
  // would vanish from every tile on this dashboard.
  const created = items.filter((s) => ["Created", "PickupAssigned"].includes(s.currentStatus)).length;
  const inTransit = items.filter((s) =>
    ["PickedUp", "AtOriginBranch", "Sorted", "InTransit", "AtDestinationBranch"].includes(s.currentStatus)
  ).length;
  const outForDelivery = items.filter((s) => s.currentStatus === "OutForDelivery").length;
  const delivered = items.filter((s) => s.currentStatus === "Delivered").length;

  const codPending = (settlements ?? [])
    .filter((s) => s.status === "Created")
    .reduce((sum, s) => sum + s.totalPayable, 0);
  const codSettled = (settlements ?? [])
    .filter((s) => s.status === "Settled")
    .reduce((sum, s) => sum + s.totalPayable, 0);

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
      <StatTile label="Created" value={String(created)} />
      <StatTile label="In Transit" value={String(inTransit)} />
      <StatTile label="Out for Delivery" value={String(outForDelivery)} />
      <StatTile label="Delivered" value={String(delivered)} />
      <StatTile label="COD Pending" value={currencyFormatter.format(codPending)} />
      <StatTile label="COD Settled" value={currencyFormatter.format(codSettled)} />
    </div>
  );
}
