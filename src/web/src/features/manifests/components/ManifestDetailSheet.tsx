"use client";

import { Spinner } from "@/components/ui/spinner";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useManifest } from "@/features/manifests/hooks/useManifest";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import { ManifestDraftSection } from "@/features/manifests/components/ManifestDraftSection";
import { ManifestSealedSection } from "@/features/manifests/components/ManifestSealedSection";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function ManifestDetailSheet() {
  const panel = useManifestPanelStore((s) => s.panel);
  const close = useManifestPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";

  const { data: manifest, isLoading, isError, refetch } = useManifest(id);
  const { data: branches } = useBranches();
  const branchName = (branchId: string) => branches?.find((b) => b.id === branchId)?.name ?? "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{manifest?.code ?? "Manifest"}</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && (
            <div className="flex items-center justify-center p-12">
              <Spinner className="h-6 w-6" />
            </div>
          )}
          {isError && (
            <ErrorState
              title="Couldn't load this manifest"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {manifest && (
            <>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1">
                  <span className="text-xs text-muted-foreground">Status</span>
                  <StatusBadge status={manifest.status} namespace="Manifest" />
                </div>
                <Field label="Purpose" value={manifest.purpose} />
                <Field label="From Branch" value={branchName(manifest.fromBranchId)} />
                <Field label="To Branch" value={branchName(manifest.toBranchId)} />
                <Field label="Shipment Count" value={String(manifest.shipmentCount)} />
                <Field label="Created At" value={new Date(manifest.createdAt).toLocaleString()} />
              </div>

              {manifest.status === "Draft" && <ManifestDraftSection manifest={manifest} />}
              {manifest.status === "Sealed" && <ManifestSealedSection manifest={manifest} />}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
