"use client";

import Link from "next/link";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { formatRs } from "@/features/cod/lib/codFormat";
import { cashByRider } from "@/features/riders/lib/riderModel";
import { formatAge } from "@/lib/dates/age";

/**
 * Cash the rider holds across every branch in the caller's COD scope (not only the page filter),
 * so "Open in COD" opens the unfiltered COD page rather than one branch.
 */
export function RiderCashCard({ riderId }: { riderId: string }) {
  const canSee = usePermission("cod.read");
  const cash = useRiderCash({}, { enabled: canSee });
  if (!canSee) return null;

  const holding = cash.data ? cashByRider(cash.data.items).get(riderId) : undefined;

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Cash held</CardTitle>
      </CardHeader>
      <CardContent className="space-y-1.5">
        {cash.isLoading ? (
          <Skeleton className="h-10 w-40" />
        ) : cash.isError ? (
          <ErrorState title="Couldn't load cash held" description="COD holdings are unavailable right now." onRetry={() => cash.refetch()} />
        ) : !holding ? (
          <p className="text-sm text-muted-foreground">No cash held</p>
        ) : (
          <>
            <p className="text-2xl font-semibold tabular-nums tracking-tight text-foreground">{formatRs(holding.amount)}</p>
            <p className="text-xs text-muted-foreground">
              {`From ${holding.parcelCount} COD ${holding.parcelCount === 1 ? "parcel" : "parcels"} · oldest collected ${formatAge(holding.oldestCollectedAt)} ago`}
            </p>
            <Link href="/cod"className="text-sm font-medium text-primary hover:underline">
              Open in COD
            </Link>
          </>
        )}
      </CardContent>
    </Card>
  );
}
