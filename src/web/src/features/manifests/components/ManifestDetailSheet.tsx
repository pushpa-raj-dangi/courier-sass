"use client";

import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useManifest } from "@/features/manifests/hooks/useManifest";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import { ManifestDraftSection } from "@/features/manifests/components/ManifestDraftSection";
import { ManifestSealedSection } from "@/features/manifests/components/ManifestSealedSection";
import { ManifestInTransitSection } from "@/features/manifests/components/ManifestInTransitSection";
import { ManifestStepper } from "@/features/manifests/components/ManifestStepper";
import { ManifestReconciliation } from "@/features/manifests/components/ManifestReconciliation";
import { ManifestCloseAction } from "@/features/manifests/components/ManifestCloseAction";

export function ManifestDetailSheet() {
  const panel = useManifestPanelStore((s) => s.panel);
  const close = useManifestPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";
  const direction = panel.mode === "detail" ? panel.direction : undefined;

  const { data: manifest, isLoading, isError, refetch } = useManifest(id);
  const names = usePeopleNames();
  const canSeal = usePermission("manifest.seal");
  const canDispatch = usePermission("manifest.dispatch");

  const receiving = manifest?.status === "InTransit" || manifest?.status === "Received";
  const reconciling = receiving || manifest?.status === "Closed";
  const eyebrow = manifest
    ? ["MANIFEST", direction?.toUpperCase(), manifest.purpose === "Return" ? "RTO" : "FORWARD"].filter(Boolean).join(" · ")
    : "MANIFEST";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="flex w-full flex-col gap-6 overflow-y-auto sm:max-w-xl">
        {!manifest && <SheetTitle className="sr-only">Manifest</SheetTitle>}
        {isLoading && <DetailSkeleton fieldRows={2} />}
        {isError && (
          <ErrorState
            title="Couldn't load this manifest"
            description="It may not exist, or you may not have access to it."
            onRetry={() => refetch()}
          />
        )}
        {manifest && (
          <>
            <SheetHeaderBlock
              eyebrow={eyebrow}
              title={manifest.code}
              mono
              status={<StatusBadge status={manifest.status} namespace="Manifest" />}
              facts={[
                { label: "From", value: names.branchName(manifest.fromBranchId) },
                { label: "To", value: names.branchName(manifest.toBranchId) },
                { label: "Vehicle", value: manifest.vehiclePlate ?? "Not assigned" },
                { label: "Driver", value: manifest.driverId ? names.riderName(manifest.driverId) : "—" },
              ]}
            />
            <ManifestStepper status={manifest.status} />

            {manifest.status === "Draft" && <ManifestDraftSection manifest={manifest} canSeal={canSeal} />}
            {manifest.status === "Sealed" && <ManifestSealedSection manifest={manifest} canDispatch={canDispatch} />}
            {receiving && <ManifestInTransitSection manifest={manifest} />}
            {reconciling && <ManifestReconciliation manifest={manifest} />}
            {receiving && <ManifestCloseAction manifest={manifest} />}
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
