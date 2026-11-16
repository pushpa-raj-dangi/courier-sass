"use client";

import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { BranchExceptions } from "@/features/branches/components/BranchExceptions";
import { BranchLocationCard } from "@/features/branches/components/BranchLocationCard";
import { BranchRightNow } from "@/features/branches/components/BranchRightNow";
import { BranchShipments } from "@/features/branches/components/BranchShipments";
import { BranchZoneControl } from "@/features/branches/components/BranchZoneControl";
import { useBranch } from "@/features/branches/hooks/useBranch";
import { useBranchActivity } from "@/features/branches/hooks/useBranchActivity";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { branchTypeLabel, zeroActivity } from "@/features/branches/lib/branchModel";
import { useBranchPanelStore } from "@/features/branches/store/branchPanelStore";
import { useDeliveryZones } from "@/features/geography/hooks/useDeliveryZones";
import { useDistricts, useLocalLevels } from "@/features/geography/hooks/useGeography";
import { useBranchDashboard } from "@/features/reports/hooks/useBranchDashboard";

export function BranchDetailSheet() {
  const panel = useBranchPanelStore((s) => s.panel);
  const close = useBranchPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const branchId = panel.mode === "detail" ? panel.branchId : "";

  const { data: branch, isLoading, isError, refetch } = useBranch(branchId);
  const { data: branches } = useBranches();
  const activity = useBranchActivity({ enabled: isOpen });
  const canReports = usePermission("report.read");
  // Shipments/Exceptions sections read the branch dashboard, which needs report.read.
  const dashboard = useBranchDashboard(canReports ? branchId : "");
  const districts = useDistricts(branch?.provinceId ?? null, isOpen);
  const localLevels = useLocalLevels(branch?.districtId ?? null, isOpen);
  const { data: zones } = useDeliveryZones();

  const districtName = branch ? districts.data?.find((d) => d.id === branch.districtId)?.name : undefined;
  const localLevelName = branch ? localLevels.data?.find((l) => l.id === branch.localLevelId)?.name : undefined;
  const address = branch
    ? [branch.addressLine, `Ward ${branch.wardNumber}`, localLevelName, districtName].filter(Boolean).join(", ")
    : "";
  const zoneName = branch?.deliveryZoneId ? zones?.find((z) => z.id === branch.deliveryZoneId)?.name ?? "—" : "Not assigned";
  const coordinates =
    branch && branch.latitude !== null && branch.longitude !== null ? `${branch.latitude.toFixed(5)}, ${branch.longitude.toFixed(5)}` : "—";
  const activityRow = activity.data ? activity.data.items.find((r) => r.branchId === branchId) ?? zeroActivity(branchId) : undefined;

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <div className="flex flex-col gap-6">
          {!branch && <SheetTitle className="sr-only">Branch</SheetTitle>}
          {isLoading && <DetailSkeleton fieldRows={2} />}
          {isError && (
            <ErrorState title="Couldn't load this branch" description="It may not exist, or you may not have access to it." onRetry={() => refetch()} />
          )}
          {branch && (
            <>
              <SheetHeaderBlock
                eyebrow={["BRANCH", branchTypeLabel(branch.type).toUpperCase(), branch.parentBranchId ? `PARENT ${branchCode(branches, branch.parentBranchId)}` : null]
                  .filter(Boolean)
                  .join(" · ")}
                title={branch.name}
                status={
                  <span className="flex items-center gap-2">
                    <span className="font-mono text-sm text-muted-foreground">{branch.code}</span>
                    <Badge variant={branch.isActive ? "outline" : "secondary"} className="font-normal">
                      {branch.isActive ? "Active" : "Inactive"}
                    </Badge>
                  </span>
                }
                facts={[
                  { label: "Address", value: address },
                  { label: "Phone", value: branch.phone ?? "—" },
                  { label: "Hours", value: branch.operatingHours ?? "—" },
                  { label: "Delivery zone", value: zoneName },
                  { label: "Coordinates", value: coordinates },
                ]}
              />
              <BranchRightNow branchId={branch.id} row={activityRow} isError={activity.isError} onRetry={() => activity.refetch()} />
              <BranchLocationCard branch={branch} address={address} />
              {canReports && dashboard.isLoading && (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4" role="status" aria-label="Loading branch dashboard">
                  {Array.from({ length: 4 }).map((_, i) => (
                    <Skeleton key={i} className="h-16 w-full rounded-lg" />
                  ))}
                </div>
              )}
              {canReports && dashboard.isError && (
                <ErrorState title="Couldn't load this branch's dashboard" description="Something went wrong fetching today's activity." onRetry={() => dashboard.refetch()} />
              )}
              {canReports && dashboard.data && (
                <>
                  <BranchShipments
                    data={{
                      shipmentCounts: dashboard.data.shipmentCounts,
                      manifestCounts: dashboard.data.manifestCounts,
                      todaysDeliveries: dashboard.data.todaysDeliveries,
                    }}
                  />
                  <BranchExceptions
                    data={{
                      missingShipmentExceptions: dashboard.data.missingShipmentExceptions,
                      openNdrCount: dashboard.data.openNdrCount,
                      riders: dashboard.data.riders,
                      codPendingFinanceVerification: dashboard.data.codPendingFinanceVerification,
                    }}
                  />
                </>
              )}
              <ComingSoonSlot
                title="Edit details, staff roster, weekly hours"
                description="Editing a branch, its staff roster and weekly opening hours will be available here."
              />
              <div className="border-t border-border pt-4">
                <BranchZoneControl branch={branch} />
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
