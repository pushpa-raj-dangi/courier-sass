import { Package, CheckCircle2, Wallet, Building2 } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import type { OrgDashboardSummary } from "@/features/reports/types";

const currencyFormatter = new Intl.NumberFormat("en-NP", {
  style: "currency",
  currency: "NPR",
  notation: "compact",
  maximumFractionDigits: 1,
});

// Every value here is re-derived from fields OrgDashboardSummary already carries — no second
// network call — so this strip can sit above the funnel/chart row as an "at a glance" summary
// without adding a new backend endpoint or a second loading state to coordinate.
export function QuickStatsStrip({ data }: { data: OrgDashboardSummary }) {
  const todaysTotal = Object.values(data.todaysShipmentsByStatus).reduce((sum, n) => sum + n, 0);
  const deliveredToday = data.todaysShipmentsByStatus["Delivered"] ?? 0;
  const activeBranchCount = Object.keys(data.branchPerformance).length;

  const tiles = [
    {
      label: "Today's Shipments",
      value: todaysTotal.toLocaleString(),
      icon: Package,
      badgeClass: "bg-muted text-muted-foreground",
    },
    {
      label: "Delivered Today",
      value: deliveredToday.toLocaleString(),
      icon: CheckCircle2,
      badgeClass: "bg-status-delivered/15 text-status-delivered",
    },
    {
      label: "COD Pending",
      value: currencyFormatter.format(data.financial.pending),
      icon: Wallet,
      badgeClass: "bg-accent text-primary",
      highlight: true,
    },
    {
      label: "Active Branches",
      value: activeBranchCount.toLocaleString(),
      icon: Building2,
      badgeClass: "bg-muted text-muted-foreground",
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
      {tiles.map((tile) => {
        const Icon = tile.icon;
        return (
          <Card key={tile.label}>
            <CardContent className="flex items-center gap-3 py-4">
              <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${tile.badgeClass}`}>
                <Icon className="h-5 w-5" strokeWidth={2} />
              </span>
              <div className="flex flex-col gap-0.5 overflow-hidden">
                <span className="truncate text-xs text-muted-foreground">{tile.label}</span>
                <span
                  className={`truncate font-sans text-lg font-semibold sm:text-xl ${
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
