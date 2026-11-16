"use client";

import dynamic from "next/dynamic";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useRunBreadcrumbs } from "@/features/rider-tracking/hooks/useRunBreadcrumbs";
import { formatMinutesAgo } from "@/features/riders/lib/riderModel";
import type { RiderBoardRow } from "@/features/riders/types";

const RouteMiniMap = dynamic(() => import("@/features/rider-tracking/components/RouteMiniMap").then((mod) => mod.RouteMiniMap), {
  ssr: false,
  loading: () => <Skeleton className="h-48 w-full rounded-lg" />,
});

export function RiderLocationCard({ row }: { row: RiderBoardRow }) {
  const canSee = usePermission("rider.location.read");
  const crumbs = useRunBreadcrumbs(row.run?.id ?? null, canSee);
  if (!canSee) return null;

  const points = crumbs.data ?? [];
  const latest = points[points.length - 1];

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Location</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {!row.run ? (
          <p className="text-sm text-muted-foreground">No location today</p>
        ) : crumbs.isLoading ? (
          <Skeleton className="h-48 w-full rounded-lg" />
        ) : crumbs.isError ? (
          <ErrorState title="Couldn't load today's route" description="Live positions are unavailable right now." onRetry={() => crumbs.refetch()} />
        ) : !latest ? (
          <p className="text-sm text-muted-foreground">No location today</p>
        ) : (
          <>
            <RouteMiniMap points={points} />
            <p className="text-xs text-muted-foreground">
              {`Last ping ${formatMinutesAgo(latest.receivedAt)}${latest.accuracyMeters === null ? "" : ` · ±${Math.round(latest.accuracyMeters)} m`} · ${points.length} ${points.length === 1 ? "ping" : "pings"} today`}
            </p>
          </>
        )}
      </CardContent>
    </Card>
  );
}
