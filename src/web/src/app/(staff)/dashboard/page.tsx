"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useOrgDashboard } from "@/features/reports/hooks/useOrgDashboard";
import { ShipmentFunnel } from "@/features/reports/components/ShipmentFunnel";
import { FinancialSummary } from "@/features/reports/components/FinancialSummary";
import { RateTiles } from "@/features/reports/components/RateTiles";
import { DeliveryRateHero } from "@/features/reports/components/DeliveryRateHero";
import { BranchPerformanceChart } from "@/features/reports/components/BranchPerformanceChart";
import { RiderPerformanceChart } from "@/features/reports/components/RiderPerformanceChart";

export default function DashboardPage() {
  const { data, isLoading, isError, refetch } = useOrgDashboard();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load the dashboard"
        description="Something went wrong fetching today's summary."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6">
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
        />
      </div>
      <FinancialSummary data={data.financial} />
    </div>
  );
}
