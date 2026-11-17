"use client";

import { useParams } from "next/navigation";
import { AlertCircle, PackageSearch, type LucideIcon } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { TrackingHeader } from "@/features/tracking/components/TrackingHeader";
import { TrackingSearchForm } from "@/features/tracking/TrackingSearchForm";
import { TrackingStatusCard } from "@/features/tracking/components/TrackingStatusCard";
import {
  useTrackShipment,
  TrackingNotFoundError,
  TrackingRateLimitedError,
} from "@/features/tracking/hooks/useTrackShipment";

function EmptyState({ icon: Icon, message }: { icon: LucideIcon; message: string }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed border-border p-10 text-center">
      <Icon className="size-8 text-muted-foreground" strokeWidth={1.5} />
      <p className="text-sm text-muted-foreground">{message}</p>
    </div>
  );
}

function updatedTime(ms: number): string {
  return new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" }).format(new Date(ms));
}

export default function TrackingResultPage() {
  const params = useParams<{ awb: string }>();
  const awb = params.awb;
  const { data, isLoading, isError, error, dataUpdatedAt } = useTrackShipment(awb);

  return (
    <div className="min-h-screen bg-background">
      <TrackingHeader showBack />
      <main className="mx-auto max-w-md space-y-4 px-4 py-6">
        <TrackingSearchForm defaultAwb={awb} />

        {isLoading && !data && (
          <div role="status" aria-label="Loading shipment" className="space-y-3">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-9 w-56" />
            <Skeleton className="h-4 w-64" />
            <Skeleton className="h-40 w-full" />
          </div>
        )}

        {!data && isError && error instanceof TrackingNotFoundError && (
          <EmptyState icon={PackageSearch} message="No shipment found for this AWB." />
        )}

        {!data && isError && error instanceof TrackingRateLimitedError && (
          <EmptyState icon={AlertCircle} message={`Too many lookups. Try again in ${error.retryAfterSeconds} seconds.`} />
        )}

        {!data && isError && !(error instanceof TrackingNotFoundError) && !(error instanceof TrackingRateLimitedError) && (
          <EmptyState icon={AlertCircle} message="Something went wrong. Please try again." />
        )}

        {data && (
          <>
            <TrackingStatusCard result={data} />
            <p className="border-t border-border pt-4 text-center text-xs text-muted-foreground">
              Tracking by Courier Platform
              {dataUpdatedAt ? ` · updated ${updatedTime(dataUpdatedAt)}` : ""}
            </p>
          </>
        )}
      </main>
    </div>
  );
}
