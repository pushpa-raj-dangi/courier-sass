"use client";

import dynamic from "next/dynamic";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { usePermission } from "@/features/auth/hooks/usePermission";
import type { Branch } from "@/features/branches/types";
import { useRiderLocations } from "@/features/rider-tracking/hooks/useRiderLocations";
import type { MapRiderPin } from "@/features/rider-tracking/lib/mapPins";
import { useRiderBoard } from "@/features/riders/hooks/useRiderBoard";
import { isLocationStale, minutesSince, riderInitials } from "@/features/riders/lib/riderModel";

const LiveMap = dynamic(() => import("@/features/rider-tracking/components/LiveMap").then((mod) => mod.LiveMap), {
  ssr: false,
  loading: () => <Skeleton className="h-48 w-full rounded-lg" />,
});

export function BranchLocationCard({ branch, address }: { branch: Branch; address: string }) {
  const canSee = usePermission("rider.location.read");
  const hasCoords = branch.latitude !== null && branch.longitude !== null;
  const showMap = canSee && hasCoords;
  const locations = useRiderLocations(showMap ? branch.id : undefined);
  const board = useRiderBoard(branch.id, { enabled: showMap });

  const now = new Date();
  const names = new Map((board.data?.items ?? []).map((r) => [r.riderId, r.fullName]));
  const pins: MapRiderPin[] = (locations.data ?? []).map((l) => {
    const name = names.get(l.riderId) ?? "Rider";
    return {
      riderId: l.riderId,
      latitude: l.latitude,
      longitude: l.longitude,
      initials: riderInitials(name),
      label: name,
      stale: isLocationStale(l.receivedAt, now),
      minutesAgo: minutesSince(l.receivedAt, now),
      popup: <span className="text-sm">{name}</span>,
    };
  });

  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="text-sm font-semibold">Location</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {showMap && (
          <LiveMap
            className="h-48"
            pins={pins}
            branch={{ latitude: branch.latitude as number, longitude: branch.longitude as number, label: branch.code?.trim() ? branch.code : branch.name }}
          />
        )}
        <p className="text-sm text-muted-foreground">{address}</p>
      </CardContent>
    </Card>
  );
}
