"use client";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { ComingSoonSlot } from "@/components/page/ComingSoonSlot";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { branchCode } from "@/features/branches/lib/branchCode";
import { RiderCashCard } from "@/features/riders/components/RiderCashCard";
import { RiderLocationCard } from "@/features/riders/components/RiderLocationCard";
import { RiderInactiveBadge, RiderStageBadge } from "@/features/riders/components/RiderStageBadge";
import { RiderTodayCard } from "@/features/riders/components/RiderTodayCard";
import { useRider } from "@/features/riders/hooks/useRider";
import { useRiderToday } from "@/features/riders/hooks/useRiderToday";
import { riderDayStage, riderInitials } from "@/features/riders/lib/riderModel";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";

export function RiderDetailSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const openAssignTask = useRiderPanelStore((s) => s.openAssignTask);
  const isOpen = panel.mode === "detail";
  const riderId = panel.mode === "detail" ? panel.riderId : "";

  const rider = useRider(riderId);
  const today = useRiderToday(riderId, isOpen);
  const { data: branches } = useBranches();
  const canAssign = usePermission("delivery.assign");

  const row = today.data?.rider;
  const isError = rider.isError || today.isError;
  const isLoading = !isError && (rider.isLoading || today.isLoading);
  const ready = Boolean(row && rider.data && today.data) && !isError;
  const baseBranchName = row ? branches?.find((b) => b.id === row.baseBranchId)?.name ?? "—" : "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <div className="flex flex-col gap-6">
          {!ready && <SheetTitle className="sr-only">Rider</SheetTitle>}
          {isLoading && <DetailSkeleton fieldRows={2} />}
          {isError && (
            <ErrorState
              title="Couldn't load this rider"
              description="They may not exist, or may be based outside your branches."
              onRetry={() => {
                rider.refetch();
                today.refetch();
              }}
            />
          )}
          {ready && row && rider.data && today.data && (
            <>
              <SheetHeaderBlock
                eyebrow={`RIDER · ${row.roleType === "PickupRider" ? "PICKUP" : "DELIVERY"} · ${branchCode(branches, row.baseBranchId)}`}
                title={
                  <span className="flex items-center gap-3">
                    <Avatar className="h-9 w-9" aria-hidden="true">
                      <AvatarFallback className="text-xs font-medium">{riderInitials(row.fullName)}</AvatarFallback>
                    </Avatar>
                    <span>{row.fullName}</span>
                  </span>
                }
                status={row.isActive ? <RiderStageBadge stage={riderDayStage(row)} /> : <RiderInactiveBadge />}
                facts={[
                  { label: "Base branch", value: baseBranchName },
                  { label: "Licence", value: rider.data.licenseNumber },
                  { label: "Vehicle", value: row.vehiclePlate ?? "Unassigned" },
                  { label: "Account", value: row.isActive ? "Active" : "Inactive" },
                ]}
              />
              <RiderTodayCard today={today.data} />
              <RiderLocationCard row={row} />
              <RiderCashCard riderId={row.riderId} baseBranchId={row.baseBranchId} />
              <ComingSoonSlot
                title="Attendance and delivery history"
                description="Shift check-ins and the last 30 days of deliveries will show here."
              />
              <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
                <p className="text-2xs text-muted-foreground">Counts are today&apos;s run only (date in UTC).</p>
                {canAssign && (
                  <Button type="button" onClick={() => openAssignTask()}>
                    Assign task
                  </Button>
                )}
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
