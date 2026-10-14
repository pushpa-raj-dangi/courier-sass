"use client";

import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useCodCollection } from "@/features/cod/hooks/useCodCollection";
import { useCodPanelStore } from "@/features/cod/store/codPanelStore";
import { collectionLabel, formatRs } from "@/features/cod/lib/codFormat";

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

function formatAmount(value: number | null): string {
  return value === null ? "—" : formatRs(value);
}

export function CollectionDetailSheet() {
  const panel = useCodPanelStore((s) => s.panel);
  const close = useCodPanelStore((s) => s.close);
  const isOpen = panel.mode === "collection-detail";
  const id = panel.mode === "collection-detail" ? panel.id : "";

  const { data: collection, isLoading, isError, refetch } = useCodCollection(id);

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>COD Collection</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-6">
          {isLoading && <DetailSkeleton fieldRows={1} />}
          {isError && (
            <ErrorState
              title="Couldn't load this COD collection"
              description="It may not exist, or you may not have access to it."
              onRetry={() => refetch()}
            />
          )}
          {collection && (
            <div className="grid grid-cols-2 gap-4">
              <div className="flex flex-col gap-1">
                <span className="text-xs text-muted-foreground">Status</span>
                <StatusBadge status={collection.status} namespace="Cod" />
              </div>
              <Field label="AWB" value={collectionLabel(collection)} />
              <Field label="Expected Amount" value={formatAmount(collection.expectedAmount)} />
              <Field label="Collected Amount" value={formatAmount(collection.collectedAmount)} />
              <Field label="Collected At" value={collection.collectedAt ? new Date(collection.collectedAt).toLocaleString() : "—"} />
              <Field label="Created At" value={new Date(collection.createdAt).toLocaleString()} />
            </div>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}
