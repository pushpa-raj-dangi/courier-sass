"use client";

import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useOrgDashboard } from "@/features/reports/hooks/useOrgDashboard";
import { ShipmentFunnel } from "@/features/reports/components/ShipmentFunnel";
import { FinancialSummary } from "@/features/reports/components/FinancialSummary";
import { RateTiles } from "@/features/reports/components/RateTiles";

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
      <ShipmentFunnel data={data.todaysShipmentsByStatus} />
      <FinancialSummary data={data.financial} />
      <RateTiles
        data={{
          overallDeliverySuccessRate: data.overallDeliverySuccessRate,
          overallNdrRate: data.overallNdrRate,
          overallRtoRate: data.overallRtoRate,
          averageDeliveryTime: data.averageDeliveryTime,
        }}
      />
    </div>
  );
}
