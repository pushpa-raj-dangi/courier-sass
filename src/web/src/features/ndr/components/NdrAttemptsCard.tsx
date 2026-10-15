"use client";

import { Truck } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { usePeopleNames } from "@/features/shipments/hooks/usePeopleNames";
import { useDeliveryAttempts } from "@/features/ndr/hooks/useDeliveryAttempts";
import { ndrReasonLabel } from "@/features/ndr/lib/ndrModel";

const attemptFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });

export function NdrAttemptsCard({ shipmentId }: { shipmentId: string }) {
  const attempts = useDeliveryAttempts(shipmentId);
  const names = usePeopleNames();

  return (
    <SectionCard title="Delivery attempts" icon={Truck}>
      {attempts.isLoading && <Skeleton className="h-20 w-full" />}
      {attempts.isError && (
        <ErrorState title="Couldn't load delivery attempts" description="Try again in a moment." onRetry={() => attempts.refetch()} />
      )}
      {attempts.data && attempts.data.items.length === 0 && <p className="text-sm text-muted-foreground">No delivery attempts recorded.</p>}
      {attempts.data && attempts.data.items.length > 0 && (
        <ol aria-label="Delivery attempts" className="divide-y divide-border">
          {attempts.data.items.map((a) => (
            <li key={`${a.attemptNumber}-${a.attemptedAt}`} className="space-y-0.5 py-2.5">
              <div className="flex items-baseline justify-between gap-3">
                <span className="text-sm font-semibold">{`Attempt ${a.attemptNumber}`}</span>
                <time dateTime={a.attemptedAt} className="whitespace-nowrap text-xs text-muted-foreground">
                  {attemptFormat.format(new Date(a.attemptedAt))}
                </time>
              </div>
              <p className="text-sm">
                {`${a.succeeded ? "Delivered" : a.reason ? ndrReasonLabel(a.reason) : "Failed"}${a.customerContacted ? " · customer contacted" : ""}`}
              </p>
              <p className="text-xs text-muted-foreground">{names.riderName(a.riderId)}</p>
              {a.notes && <p className="text-xs text-muted-foreground">{a.notes}</p>}
              {a.nextAction && <p className="text-xs text-muted-foreground">{`Next: ${a.nextAction}`}</p>}
            </li>
          ))}
        </ol>
      )}
    </SectionCard>
  );
}
