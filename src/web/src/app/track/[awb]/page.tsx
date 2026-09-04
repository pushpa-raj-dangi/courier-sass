"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { AlertCircle, PackageSearch, Search } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Separator } from "@/components/ui/separator";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { TrackingTimeline } from "@/features/tracking/components/TrackingTimeline";
import {
  useTrackShipment,
  TrackingNotFoundError,
  TrackingRateLimitedError,
} from "@/features/tracking/hooks/useTrackShipment";

export default function TrackingResultPage() {
  const params = useParams<{ awb: string }>();
  const awb = params.awb;
  const { data, isLoading, isError, error } = useTrackShipment(awb);

  return (
    <main className="flex min-h-screen flex-col items-center gap-6 bg-background p-4 py-12">
      <Link href="/track" className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground">
        <Search className="h-4 w-4" strokeWidth={2} />
        Track another shipment
      </Link>
      <Card className="w-full max-w-lg shadow-xl shadow-primary/5">
        <CardHeader>
          <CardTitle className="font-display text-xl">AWB {awb}</CardTitle>
        </CardHeader>
        <CardContent>
          {isLoading && (
            <div className="space-y-3">
              <Skeleton className="h-6 w-32" />
              <Skeleton className="h-16 w-full" />
              <Skeleton className="h-16 w-full" />
            </div>
          )}

          {isError && error instanceof TrackingNotFoundError && (
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <PackageSearch className="h-8 w-8 text-muted-foreground" strokeWidth={1.5} />
              <p className="text-sm text-muted-foreground">No shipment found for this AWB.</p>
            </div>
          )}

          {isError && error instanceof TrackingRateLimitedError && (
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <AlertCircle className="h-8 w-8 text-muted-foreground" strokeWidth={1.5} />
              <p className="text-sm text-muted-foreground">
                Too many lookups. Try again in {error.retryAfterSeconds} seconds.
              </p>
            </div>
          )}

          {isError && !(error instanceof TrackingNotFoundError) && !(error instanceof TrackingRateLimitedError) && (
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <AlertCircle className="h-8 w-8 text-muted-foreground" strokeWidth={1.5} />
              <p className="text-sm text-muted-foreground">Something went wrong. Please try again.</p>
            </div>
          )}

          {data && (
            <div className="space-y-4">
              <StatusBadge status={data.currentStatus} namespace="Public" />
              <Separator />
              <TrackingTimeline events={data.events} />
            </div>
          )}
        </CardContent>
      </Card>
    </main>
  );
}
