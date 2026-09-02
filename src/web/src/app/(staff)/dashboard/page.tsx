"use client";

import { useState } from "react";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useOrgDashboard } from "@/features/reports/hooks/useOrgDashboard";
import { DashboardHeader } from "@/features/reports/components/DashboardHeader";
import { QuickStatsStrip } from "@/features/reports/components/QuickStatsStrip";
import { ShipmentFunnel } from "@/features/reports/components/ShipmentFunnel";
import { FinancialSummary } from "@/features/reports/components/FinancialSummary";
import { RateTiles } from "@/features/reports/components/RateTiles";
import { DeliveryRateHero } from "@/features/reports/components/DeliveryRateHero";
import { BranchPerformanceChart } from "@/features/reports/components/BranchPerformanceChart";
import { RiderPerformanceChart } from "@/features/reports/components/RiderPerformanceChart";
import { ExportReportsCard } from "@/features/reports/components/ExportReportsCard";

export default function DashboardPage() {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  // UTC-anchored before leaving the browser — a bare "YYYY-MM-DD" lets .NET's DateTimeOffset
  // deserializer default to the API host's local offset (Nepal, UTC+5:45), which Npgsql's
  // timestamptz reads/writes reject outright (the exact bug Frontend Phase F7 hit and fixed).
  const { data, isLoading, isError, refetch } = useOrgDashboard({
    from: from ? `${from}T00:00:00Z` : undefined,
    to: to ? `${to}T23:59:59Z` : undefined,
  });
  const rangeLabel = from && to ? `${from} – ${to}` : "Last 30 days";

  return (
    <div className="space-y-6">
      <DashboardHeader />

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <Label htmlFor="dashboard-from">From</Label>
          <DatePicker id="dashboard-from" aria-label="From date" value={from} onChange={setFrom} clearable />
        </div>
        <div className="space-y-1">
          <Label htmlFor="dashboard-to">To</Label>
          <DatePicker id="dashboard-to" aria-label="To date" value={to} onChange={setTo} clearable />
        </div>
      </div>

      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && (isError || !data) && (
        <ErrorState
          title="Couldn't load the dashboard"
          description="Something went wrong fetching today's summary."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && data && (
        <>
          <QuickStatsStrip data={data} />
          <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
            <ShipmentFunnel data={data.todaysShipmentsByStatus} />
            <BranchPerformanceChart data={data.branchPerformance} />
            <RiderPerformanceChart data={data.riderPerformance} />
          </div>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
            <DeliveryRateHero rate={data.overallDeliverySuccessRate} />
            <RateTiles
              data={{
                overallNdrRate: data.overallNdrRate,
                overallRtoRate: data.overallRtoRate,
                averageDeliveryTime: data.averageDeliveryTime,
              }}
              rangeLabel={rangeLabel}
            />
          </div>
          <FinancialSummary data={data.financial} />
          <ExportReportsCard />
        </>
      )}
    </div>
  );
}
