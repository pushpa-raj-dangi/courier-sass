"use client";

import { useState } from "react";
import { Download, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip } from "@/components/page/PipelineStrip";
import { KpiTile } from "@/components/page/KpiTile";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useOrgDashboard } from "@/features/reports/hooks/useOrgDashboard";
import { useShipmentsSummary } from "@/features/shipments/hooks/useShipmentsSummary";
import { useShipmentPanelStore } from "@/features/shipments/store/shipmentPanelStore";
import { ExportReportsCard } from "@/features/reports/components/ExportReportsCard";
import { DashboardRangeControl } from "@/features/reports/components/dashboard/DashboardRangeControl";
import { NeedsActionCard } from "@/features/reports/components/dashboard/NeedsActionCard";
import { CodRangeCard } from "@/features/reports/components/dashboard/CodRangeCard";
import { BranchPerformanceTable } from "@/features/reports/components/dashboard/BranchPerformanceTable";
import { RiderPerformanceTable } from "@/features/reports/components/dashboard/RiderPerformanceTable";
import {
  buildPipelineStages, formatRate, formatTimeSpan, rangeLabel, rangeToApi, type DashboardRange,
} from "@/features/reports/lib/dashboardModel";

export default function DashboardPage() {
  const [range, setRange] = useState<DashboardRange>({ preset: "30d", from: "", to: "" });
  const org = useOrgDashboard(rangeToApi(range));
  const summary = useShipmentsSummary();
  const { data: branches } = useBranches();
  const canCreate = usePermission("shipment.create");
  const openCreate = useShipmentPanelStore((s) => s.openCreate);
  const label = rangeLabel(range);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Operations overview"
        description={branches ? `${branches.length} branches in scope` : undefined}
        actions={
          <>
            <DashboardRangeControl value={range} onChange={setRange} />
            <Popover>
              <PopoverTrigger asChild>
                <Button variant="outline" className="gap-1.5">
                  <Download className="h-4 w-4" aria-hidden="true" />
                  Export
                </Button>
              </PopoverTrigger>
              <PopoverContent align="end" className="w-[min(92vw,420px)] p-0">
                <ExportReportsCard />
              </PopoverContent>
            </Popover>
            {canCreate && (
              <Button className="gap-1.5" onClick={() => openCreate()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                Book shipment
              </Button>
            )}
          </>
        }
      />

      <section className="space-y-3">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="font-display text-base font-semibold">Parcel pipeline</h2>
          <span className="text-xs text-muted-foreground">Parcels currently in each stage · delivered {label.toLowerCase()}</span>
        </div>
        {summary.isError ? (
          <ErrorState title="Couldn't load the pipeline" description="Current status counts are unavailable." />
        ) : (
          <PipelineStrip ariaLabel="Parcel pipeline" stages={buildPipelineStages(summary.data, org.data?.branchPerformance)} />
        )}
      </section>

      {org.isLoading && (
        <div role="status" aria-live="polite" className="grid grid-cols-2 gap-4 lg:grid-cols-4">
          <span className="sr-only">Loading</span>
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}
        </div>
      )}
      {!org.isLoading && (org.isError || !org.data) && (
        <ErrorState title="Couldn't load performance data" description="Rates, COD and branch performance are unavailable." onRetry={() => org.refetch()} />
      )}
      {org.data && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <KpiTile label="Delivery success rate" value={formatRate(org.data.overallDeliverySuccessRate)} note={label} />
          <KpiTile label="NDR rate" value={formatRate(org.data.overallNdrRate)} note={label} />
          <KpiTile label="RTO rate" value={formatRate(org.data.overallRtoRate)} note={label} />
          <KpiTile label="Avg. booking to delivery" value={formatTimeSpan(org.data.averageDeliveryTime)} note={label} />
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <ComingSoonSlot title="Hourly flow" description="Booked vs delivered parcels by hour of the day." minHeight={260} />
        <NeedsActionCard />
      </div>

      {org.data && (
        <>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
            <CodRangeCard financial={org.data.financial} rangeLabel={label} />
            <BranchPerformanceTable data={org.data.branchPerformance} />
          </div>
          <RiderPerformanceTable data={org.data.riderPerformance} />
        </>
      )}
    </div>
  );
}
