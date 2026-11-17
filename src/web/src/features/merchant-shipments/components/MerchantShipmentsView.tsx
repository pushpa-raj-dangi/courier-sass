"use client";

import { Plus, Search, Truck } from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { PageHeader } from "@/components/page/PageHeader";
import { ErrorState } from "@/components/states/ErrorState";
import { SectionError } from "@/components/states/SectionError";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { cn } from "@/lib/utils";
import { sumCounts } from "@/features/merchant-dashboard/lib/dashboardLogic";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { useMerchantBranches } from "@/features/merchant-shipments/hooks/useMerchantBranches";
import { useMerchantServiceTypes } from "@/features/merchant-shipments/hooks/useMerchantServiceTypes";
import { useMerchantShipmentCounts } from "@/features/merchant-shipments/hooks/useMerchantShipmentCounts";
import { useMerchantShipments } from "@/features/merchant-shipments/hooks/useMerchantShipments";
import { shipmentsDescription } from "@/features/merchant-shipments/lib/listView";
import {
  MERCHANT_SHIPMENT_TABS,
  MERCHANT_STATUS_GROUPS,
  type MerchantShipmentTab,
} from "@/features/merchant-shipments/lib/statusGroups";
import { useMerchantShipmentPanelStore } from "@/features/merchant-shipments/store/merchantShipmentPanelStore";
import { MerchantShipmentsTable } from "./MerchantShipmentsTable";
import { useDebouncedSearch } from "./useDebouncedSearch";
import { useMerchantListUrlState } from "./useMerchantListUrlState";

const ANY = "any";

function tabLabel(tab: MerchantShipmentTab) {
  return tab === "all" ? "All" : MERCHANT_STATUS_GROUPS[tab].label;
}

export function MerchantShipmentsView() {
  const { params, patch, setPage } = useMerchantListUrlState();
  const { data, isLoading, isError, refetch } = useMerchantShipments(params);
  const counts = useMerchantShipmentCounts();
  const queryClient = useQueryClient();
  const { data: branches } = useMerchantBranches();
  const { data: serviceTypes } = useMerchantServiceTypes();
  const canBook = useMerchantPermission("shipment.create");
  const canRequestPickup = useMerchantPermission("merchant.pickups.create");
  const openCreateShipment = useMerchantShipmentPanelStore((s) => s.openCreate);
  const openDetail = useMerchantShipmentPanelStore((s) => s.openDetail);
  const openCreatePickup = useMerchantPickupPanelStore((s) => s.openCreate);
  const search = useDebouncedSearch(params.q, (q) => patch({ q }));

  const onTheWay = sumCounts([counts.booked.count, counts.inTransit.count, counts.outForDelivery.count]);
  const countsFailed = MERCHANT_SHIPMENT_TABS.some((t) => !counts[t].isLoading && counts[t].count === null);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Shipments"
        description={shipmentsDescription(counts.all.count, onTheWay)}
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

      <Tabs value={params.tab} onValueChange={(tab) => patch({ tab: tab as MerchantShipmentTab })}>
        <TabsList className="w-full justify-start overflow-x-auto">
          {MERCHANT_SHIPMENT_TABS.map((tab) => {
            const { count, isLoading: countLoading } = counts[tab];
            const urgent = tab === "needsAttention" && count !== null && count > 0;
            return (
              <TabsTrigger key={tab} value={tab} className="gap-2">
                {tabLabel(tab)}
                {countLoading ? (
                  <Skeleton data-testid="tab-count-skeleton" className="h-4 w-5 rounded-full" />
                ) : (
                  <span
                    className={cn(
                      "rounded-full px-1.5 text-2xs tabular-nums",
                      urgent ? "bg-destructive/10 text-destructive" : "bg-muted text-muted-foreground"
                    )}
                  >
                    {count === null ? "—" : count.toLocaleString("en-US")}
                  </span>
                )}
              </TabsTrigger>
            );
          })}
        </TabsList>
      </Tabs>
      {countsFailed && (
        <SectionError
          message="Couldn't load some shipment counts."
          onRetry={() => queryClient.invalidateQueries({ queryKey: ["merchant-shipments", "count"] })}
        />
      )}

      <div role="toolbar" aria-label="Shipment filters" className="flex flex-wrap items-center gap-2">
        <div className="relative w-full sm:w-72">
          <Search className="pointer-events-none absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" aria-hidden="true" />
          <Input
            type="search"
            aria-label="Search shipments"
            placeholder="Search by AWB, name or phone"
            value={search.text}
            onChange={(e) => search.onChange(e.target.value)}
            className="h-9 pl-8"
          />
        </div>
        <DatePicker
          id="merchant-shipments-from"
          aria-label="Booked from"
          placeholder="Booked from"
          value={params.from ?? ""}
          onChange={(from) => patch({ from: from || null })}
          clearable
        />
        <DatePicker
          id="merchant-shipments-to"
          aria-label="Booked to"
          placeholder="Booked to"
          value={params.to ?? ""}
          onChange={(to) => patch({ to: to || null })}
          clearable
        />
        <Select value={params.serviceTypeId ?? ANY} onValueChange={(v) => patch({ serviceTypeId: v === ANY ? null : v })}>
          <SelectTrigger aria-label="Service" className="h-9 w-44">
            <SelectValue placeholder="Service" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ANY}>Service: Any</SelectItem>
            {(serviceTypes ?? []).map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {isError ? (
        <ErrorState title="Couldn't load shipments" description="Something went wrong fetching your shipments." onRetry={() => refetch()} />
      ) : (
        <MerchantShipmentsTable
          data={data}
          isLoading={isLoading}
          branches={branches}
          serviceTypes={serviceTypes}
          onPageChange={setPage}
          onOpen={openDetail}
        />
      )}
    </div>
  );
}
