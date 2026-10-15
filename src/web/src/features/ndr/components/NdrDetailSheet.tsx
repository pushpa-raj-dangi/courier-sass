"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetTitle } from "@/components/ui/sheet";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useMerchants } from "@/features/shipments/hooks/useMerchants";
import { useNdr } from "@/features/ndr/hooks/useNdr";
import { useDeliveryAttempts } from "@/features/ndr/hooks/useDeliveryAttempts";
import { useNdrPanelStore } from "@/features/ndr/store/ndrPanelStore";
import { NdrAttemptsCard } from "@/features/ndr/components/NdrAttemptsCard";
import { NdrFollowUpCard } from "@/features/ndr/components/NdrFollowUpCard";
import { NdrResolveCard } from "@/features/ndr/components/NdrResolveCard";
import { NdrResolvedSummary } from "@/features/ndr/components/NdrResolvedSummary";
import { formatAge } from "@/lib/dates/age";

const openedFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

export function NdrDetailSheet() {
  const panel = useNdrPanelStore((s) => s.panel);
  const close = useNdrPanelStore((s) => s.close);
  const isOpen = panel.mode === "detail";
  const id = panel.mode === "detail" ? panel.id : "";
  const [notice, setNotice] = useState<{ id: string; text: string } | null>(null);

  const { data: ndr, isLoading, isError, refetch } = useNdr(id);
  // The NDR list does not serve recipient/merchant/COD (follow-up 5) — read them from the shipment.
  const shipment = useShipment(ndr?.awb ?? "");
  const attempts = useDeliveryAttempts(ndr?.shipmentId ?? "");
  const { data: merchants } = useMerchants();

  const noticeText = notice?.id === id ? notice.text : null;
  const merchantName = shipment.data ? merchants?.find((m) => m.id === shipment.data!.merchantId)?.displayName ?? "—" : "—";

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <TooltipProvider>
          <div className="flex flex-col gap-6">
            {!ndr && <SheetTitle className="sr-only">NDR</SheetTitle>}
            {isLoading && <DetailSkeleton fieldRows={1} />}
            {isError && (
              <ErrorState title="Couldn't load this NDR" description="It may not exist, or you may not have access to it." onRetry={() => refetch()} />
            )}
            {ndr && (
              <>
                <SheetHeaderBlock
                  eyebrow={`NDR · Opened ${openedFormat.format(new Date(ndr.createdAt))} · ${formatAge(ndr.createdAt)}`}
                  title={ndr.awb}
                  mono
                  status={<StatusBadge status={ndr.status} namespace="Ndr" />}
                  facts={[
                    { label: "Recipient", value: shipment.data?.recipientName ?? "—" },
                    { label: "Merchant", value: merchantName },
                    { label: "COD", value: shipment.data ? formatRs(shipment.data.codAmount) : "—" },
                    { label: "Attempts", value: attempts.data ? String(attempts.data.items.length) : "—" },
                  ]}
                />
                {shipment.isError && (
                  <p className="text-xs text-muted-foreground">
                    Parcel details are unavailable.{" "}
                    <Button type="button" variant="link" size="sm" className="h-auto p-0 text-xs" onClick={() => shipment.refetch()}>
                      Retry
                    </Button>
                  </p>
                )}
                {noticeText && <p role="status" className="text-sm font-medium text-foreground">{noticeText}</p>}

                <NdrAttemptsCard shipmentId={ndr.shipmentId} />

                {ndr.status === "Resolved" ? (
                  <NdrResolvedSummary ndr={ndr} />
                ) : (
                  <>
                    <NdrFollowUpCard key={`follow-up-${ndr.status}`} ndr={ndr} onSaved={() => setNotice({ id, text: "Follow-up saved." })} />
                    <NdrResolveCard key={`resolve-${ndr.status}`} ndr={ndr} onResolved={() => setNotice({ id, text: "NDR resolved." })} />
                  </>
                )}
              </>
            )}
          </div>
        </TooltipProvider>
      </SheetContent>
    </Sheet>
  );
}
