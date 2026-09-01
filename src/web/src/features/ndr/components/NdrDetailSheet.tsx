"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useNdr } from "@/features/ndr/hooks/useNdr";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { NdrUpdateSection } from "@/features/ndr/components/NdrUpdateSection";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function NdrDetailSheet() {
  const panel = useNdrPanelStore((s) => s.panel);
  const close = useNdrPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: ndr, isLoading, isError, refetch } = useNdr(id);

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{ndr?.awb ?? "NDR"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this NDR"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {ndr && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={ndr.status} namespace="Ndr" />
                </div>
                <Field label="Reason" value={ndr.reason} />
                <Field label="Assigned Staff" value={ndr.assignedStaffUserId ?? "Unassigned"} />
                <Field label="Created At" value={new Date(ndr.createdAt).toLocaleString()} />
              </div>

              {(ndr.status === "Open" || ndr.status === "Contacting" || ndr.status === "CustomerConfirmed") && (
                <NdrUpdateSection ndr={ndr} />
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
