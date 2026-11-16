"use client";

import dynamic from "next/dynamic";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useRiderLocations, type RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";
import type { MapBranchPin, MapRiderPin } from "@/features/rider-tracking/lib/mapPins";
import type { Branch } from "@/features/branches/types";
import type { RiderBoardRow } from "@/features/riders/types";
import {
  STALE_LOCATION_MINUTES, formatMinutesAgo, isLocationStale, minutesSince, riderDayStage, riderInitials, roleLabel,
  type RiderCashTotal,
} from "@/features/riders/lib/riderModel";

const LiveMap = dynamic(() => import("@/features/rider-tracking/components/LiveMap").then((mod) => mod.LiveMap), {
  ssr: false,
  loading: () => <Skeleton className="h-[480px] w-full rounded-lg" />,
});

const updatedFormat = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit" });

function PinPopup({
  location, row, cash, showCash, onOpenRider, now,
}: {
  location: RiderLocation;
  row?: RiderBoardRow;
  cash?: RiderCashTotal;
  showCash: boolean;
  onOpenRider: (riderId: string) => void;
  now: Date;
}) {
  const accuracy = location.accuracyMeters === null ? "" : ` · ±${Math.round(location.accuracyMeters)} m`;
  return (
    <div className="min-w-44 space-y-1">
      <p className="text-sm font-medium text-foreground">{row?.fullName ?? "Rider"}</p>
      {row && <p className="text-xs text-muted-foreground">{`${roleLabel(row.roleType)} · ${row.vehiclePlate ?? "No vehicle"}`}</p>}
      <p className="text-xs text-foreground">{row?.run ? `${row.tasksCompleted} of ${row.tasksAssigned} done` : "No run today"}</p>
      <p className="text-xs text-muted-foreground">{`Last ping ${formatMinutesAgo(location.receivedAt, now)}${accuracy}`}</p>
      {showCash && <p className="text-xs text-foreground">{`Cash held ${cash ? formatRs(cash.amount) : "—"}`}</p>}
      <Button type="button" size="sm" variant="outline" className="mt-1 h-7" onClick={() => onOpenRider(location.riderId)}>
        Open rider
      </Button>
    </div>
  );
}

export function RidersMapTab({
  branchId, branch, rows, cash, showCash, onOpenRider, now,
}: {
  branchId?: string;
  branch?: Branch;
  rows?: RiderBoardRow[];
  cash?: Map<string, RiderCashTotal>;
  showCash: boolean;
  onOpenRider: (riderId: string) => void;
  now: Date;
}) {
  // Mounted only while the Map tab is open, so the 20 s polling stops on the other tabs.
  const locations = useRiderLocations(branchId);

  if (!branchId) {
    return <EmptyState title="Pick a branch to see its riders on the map" description="The map shows one branch at a time. Choose a branch above." />;
  }
  if (locations.isError) {
    return <ErrorState title="Couldn't load rider locations" description="Something went wrong fetching live positions." onRetry={() => locations.refetch()} />;
  }
  if (locations.isLoading || !locations.data) {
    return <Skeleton role="status" aria-label="Loading rider map" className="h-[480px] w-full rounded-lg" />;
  }

  const rowsById = new Map((rows ?? []).map((r) => [r.riderId, r]));
  const located = new Set(locations.data.map((l) => l.riderId));
  const pins: MapRiderPin[] = locations.data.map((location) => {
    const row = rowsById.get(location.riderId);
    const name = row?.fullName ?? "Rider";
    return {
      riderId: location.riderId,
      latitude: location.latitude,
      longitude: location.longitude,
      initials: riderInitials(name),
      label: name,
      stale: isLocationStale(location.receivedAt, now),
      minutesAgo: minutesSince(location.receivedAt, now),
      popup: <PinPopup location={location} row={row} cash={cash?.get(location.riderId)} showCash={showCash} onOpenRider={onOpenRider} now={now} />,
    };
  });
  const branchPin: MapBranchPin | undefined =
    branch && branch.latitude !== null && branch.longitude !== null
      ? { latitude: branch.latitude, longitude: branch.longitude, label: branch.code?.trim() ? branch.code : branch.name }
      : undefined;
  const notOnMap = (rows ?? []).filter(
    (r) => r.isActive && r.baseBranchId === branchId && riderDayStage(r) === "on" && !located.has(r.riderId),
  );

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-4 text-xs text-muted-foreground">
        <span className="flex items-center gap-1.5">
          <span className="h-3 w-3 rounded-full bg-primary" aria-hidden="true" />
          Fresh ping
        </span>
        <span className="flex items-center gap-1.5">
          <span className="h-3 w-3 rounded-full border-2 border-dashed border-muted-foreground" aria-hidden="true" />
          {`No ping for over ${STALE_LOCATION_MINUTES} min`}
        </span>
        <span className="ml-auto">{`Updated ${updatedFormat.format(new Date(locations.dataUpdatedAt))} · refreshes every 20 s`}</span>
      </div>
      {pins.length === 0 && <p className="text-sm text-muted-foreground">No rider at this branch has sent a position yet.</p>}
      <LiveMap pins={pins} branch={branchPin} />
      {notOnMap.length > 0 && (
        <section aria-label="On run, not on the map" className="rounded-lg border border-border bg-card p-4">
          <h3 className="text-sm font-semibold text-foreground">On run, not on the map</h3>
          <p className="mt-0.5 text-xs text-muted-foreground">These riders have not sent a position from the rider app.</p>
          <ul className="mt-3 space-y-1.5">
            {notOnMap.map((r) => (
              <li key={r.riderId} className="flex items-center justify-between gap-3 text-sm">
                <span>{r.fullName}</span>
                <Button type="button" variant="link" size="sm" className="h-auto p-0" aria-label={`Open ${r.fullName}`} onClick={() => onOpenRider(r.riderId)}>
                  Open rider
                </Button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
