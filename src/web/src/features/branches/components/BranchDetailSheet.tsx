"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranch } from "@/features/branches/hooks/useBranch";
import { useBranchDashboard } from "@/features/reports/hooks/useBranchDashboard";
import { BranchRecord } from "@/features/branches/BranchRecord";
import { BranchShipments } from "@/features/branches/components/BranchShipments";
import { BranchExceptions } from "@/features/branches/components/BranchExceptions";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";

export function BranchDetailSheet() {
  const panel = useBranchPanelStore((s) => s.panel);
  const close = useBranchPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const branchId = panel.mode === "detail" ? panel.branchId : "";

  const { data: branch, isLoading: isBranchLoading, isError: isBranchError, refetch: refetchBranch } = useBranch(branchId);
  const {
    data: dashboard,
    isLoading: isDashboardLoading,
    isError: isDashboardError,
    refetch: refetchDashboard,
  } = useBranchDashboard(branchId);

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{branch?.name ?? "Branch"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isBranchLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isBranchError && (
            <ErrorState
              title="Couldn't load this branch"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetchBranch()}
            />
          )}
          {branch && (
            <>
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
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
