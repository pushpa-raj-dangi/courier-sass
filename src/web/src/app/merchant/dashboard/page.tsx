"use client";

import { Plus, Truck } from "lucide-react";
import { PageHeader } from "@/components/page/PageHeader";
import { Button } from "@/components/ui/button";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import { MoneyBand } from "@/features/merchant-dashboard/components/MoneyBand";
import { NeedsAttention } from "@/features/merchant-dashboard/components/NeedsAttention";
import { ParcelsNow } from "@/features/merchant-dashboard/components/ParcelsNow";
import { RecentShipments } from "@/features/merchant-dashboard/components/RecentShipments";
import { firstName, greetingFor, sumCounts } from "@/features/merchant-dashboard/lib/dashboardLogic";
import { useMerchantShipmentCounts } from "@/features/merchant-shipments/hooks/useMerchantShipmentCounts";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";

export default function MerchantDashboardPage() {
  const { data: session } = useMerchantSession();
  const counts = useMerchantShipmentCounts();
  const canReadCod = useMerchantPermission("merchant.cod.read");
  const canBook = useMerchantPermission("shipment.create");
  const canRequestPickup = useMerchantPermission("merchant.pickups.create");
  const openCreateShipment = useMerchantShipmentPanelStore((s) => s.openCreate);
  const openCreatePickup = useMerchantPickupPanelStore((s) => s.openCreate);

  const onTheWay = sumCounts([counts.booked.count, counts.inTransit.count, counts.outForDelivery.count]);
  const name = firstName(session?.fullName);

  return (
    <div className="space-y-6">
      <PageHeader
        title={session ? `Good ${greetingFor(new Date().getHours())}, ${name}` : "Dashboard"}
        description={session ? `${session.merchantDisplayName} · ${onTheWay ?? "—"} parcels on the way` : undefined}
        actions={
          <>
            {canRequestPickup && (
              <Button variant="outline" size="sm" className="gap-1.5" onClick={() => openCreatePickup()}>
                <Truck className="h-4 w-4" strokeWidth={2} />
                Request pickup
              </Button>
            )}
            {canBook && (
              <Button size="sm" className="gap-1.5" onClick={() => openCreateShipment()}>
                <Plus className="h-4 w-4" strokeWidth={2} />
                Book shipment
              </Button>
            )}
          </>
        }
      />
      {canReadCod && <MoneyBand />}
      <div className="grid gap-4 lg:grid-cols-3">
        <ParcelsNow canReadCod={canReadCod} />
        <NeedsAttention />
      </div>
      <RecentShipments />
    </div>
  );
}
