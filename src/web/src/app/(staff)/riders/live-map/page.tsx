"use client";

import dynamic from "next/dynamic";
import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { LoadingState } from "@/components/states/LoadingState";
import { useUiStore } from "@/lib/store/uiStore";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useRiderLocations } from "@/features/rider-tracking/hooks/useRiderLocations";

const LiveMap = dynamic(() => import("@/features/rider-tracking/components/LiveMap").then((mod) => mod.LiveMap), { ssr: false });

export default function LiveMapPage() {
  const activeBranchId = useUiStore((s) => s.activeBranchId);
  const { data: locations, isLoading, isError, refetch } = useRiderLocations(activeBranchId ?? undefined);
  const { data: riders } = useRiders();

  const riderLicenseById = new Map((riders ?? []).map((r) => [r.id, r.licenseNumber]));

  return (
    <div className="space-y-4">
      <h1 className="font-display text-xl font-semibold">Live Map</h1>
      {!activeBranchId ? (
        <EmptyState title="Select a branch" description="Choose a branch from the switcher to see its active riders." />
      ) : isError ? (
        <ErrorState title="Couldn't load rider locations" description="Something went wrong fetching live positions." onRetry={() => refetch()} />
      ) : isLoading ? (
        <LoadingState />
      ) : locations && locations.length > 0 ? (
        <LiveMap locations={locations} riderLicenseById={riderLicenseById} />
      ) : (
        <EmptyState title="No active riders" description="No riders at this branch have reported a position yet." />
      )}
    </div>
  );
}
