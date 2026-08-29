"use client";

import { useParams } from "next/navigation";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranch } from "@/features/branches/hooks/useBranch";
import { useBranchDashboard } from "@/features/reports/hooks/useBranchDashboard";
import { BranchRecord } from "@/features/branches/BranchRecord";
import { BranchShipments } from "@/features/branches/components/BranchShipments";
import { BranchExceptions } from "@/features/branches/components/BranchExceptions";

export default function BranchDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data: branch, isLoading: isBranchLoading, isError: isBranchError, refetch: refetchBranch } = useBranch(id);
  const {
    data: dashboard,
    isLoading: isDashboardLoading,
    isError: isDashboardError,
    refetch: refetchDashboard,
  } = useBranchDashboard(id);

  if (isBranchLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isBranchError || !branch) {
    return (
      <ErrorState
        title="Couldn't load this branch"
        description="It may not exist, or you may not have access to it."
        onRetry={() => refetchBranch()}
      />
    );
  }

  return (
    <div className="space-y-6">
      <BranchRecord branch={branch} />
      {isDashboardLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {isDashboardError && (
        <ErrorState
          title="Couldn't load this branch's dashboard"
          description="Something went wrong fetching today's activity."
          onRetry={() => refetchDashboard()}
        />
      )}
      {dashboard && (
        <>
          <BranchShipments
            data={{
              shipmentCounts: dashboard.shipmentCounts,
              manifestCounts: dashboard.manifestCounts,
              todaysDeliveries: dashboard.todaysDeliveries,
            }}
          />
          <BranchExceptions
            data={{
              missingShipmentExceptions: dashboard.missingShipmentExceptions,
              openNdrCount: dashboard.openNdrCount,
              riders: dashboard.riders,
              codPendingFinanceVerification: dashboard.codPendingFinanceVerification,
            }}
          />
        </>
      )}
    </div>
  );
}
