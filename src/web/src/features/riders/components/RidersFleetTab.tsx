"use client";

import { Button } from "@/components/ui/button";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { ErrorState } from "@/components/states/ErrorState";
import { RiderStageBadge } from "@/features/riders/components/RiderStageBadge";
import { riderDayStage, type FleetFilter } from "@/features/riders/lib/riderModel";
import type { RiderBoardRow, Vehicle } from "@/features/riders/types";
import { useFilterPage } from "@/lib/hooks/useFilterPage";
import { toClientPage } from "@/lib/paging/clientPage";

const PAGE_SIZE = 20;

export function RidersFleetTab({
  vehicles, isLoading, isError, onRetry, rows, filter, onFilterChange,
}: {
  vehicles?: Vehicle[];
  isLoading: boolean;
  isError: boolean;
  onRetry: () => void;
  rows?: RiderBoardRow[];
  filter: FleetFilter;
  onFilterChange: (filter: FleetFilter) => void;
}) {
  const [page, setPage] = useFilterPage(filter);
  const rowsById = new Map((rows ?? []).map((r) => [r.riderId, r]));
  const unassigned = (vehicles ?? []).filter((v) => v.riderId === null);
  const shown = filter === "unassigned" ? unassigned : vehicles ?? [];

  const columns: ColumnDef<Vehicle>[] = [
    { key: "plate", header: "Plate", render: (v) => <span className="font-mono text-sm">{v.plateNumber}</span> },
    { key: "type", header: "Type", render: (v) => <span className="text-sm">{v.type}</span> },
    { key: "capacity", header: "Capacity", render: (v) => <span className="text-sm tabular-nums">{v.capacity === null ? "—" : `${v.capacity} kg`}</span> },
    {
      key: "rider",
      header: "Assigned rider",
      render: (v) =>
        v.riderId === null ? (
          <span className="text-sm text-muted-foreground">Unassigned</span>
        ) : (
          <span className="text-sm">{rowsById.get(v.riderId)?.fullName ?? "—"}</span>
        ),
    },
    {
      key: "today",
      header: "Rider today",
      render: (v) => {
        const row = v.riderId ? rowsById.get(v.riderId) : undefined;
        if (!row) return <span className="text-sm text-muted-foreground">—</span>;
        return (
          <span className="flex items-center gap-2">
            <RiderStageBadge stage={riderDayStage(row)} />
            {row.run && <span className="text-xs tabular-nums text-muted-foreground">{`${row.tasksCompleted} of ${row.tasksAssigned}`}</span>}
          </span>
        );
      },
    },
  ];

  return (
    <div className="space-y-3">
      <div role="group" aria-label="Vehicle filter" className="flex flex-wrap items-center gap-2">
        <Button type="button" size="sm" variant={filter === "all" ? "secondary" : "outline"} aria-pressed={filter === "all"} onClick={() => onFilterChange("all")}>
          {vehicles ? `All ${vehicles.length}` : "All"}
        </Button>
        <Button type="button" size="sm" variant={filter === "unassigned" ? "secondary" : "outline"} aria-pressed={filter === "unassigned"} onClick={() => onFilterChange("unassigned")}>
          {vehicles ? `Unassigned ${unassigned.length}` : "Unassigned"}
        </Button>
      </div>
      {isError ? (
        <ErrorState title="Couldn't load vehicles" description="Something went wrong fetching the fleet." onRetry={onRetry} />
      ) : (
        <DataTable
          columns={columns}
          data={vehicles && toClientPage(shown, page, PAGE_SIZE)}
          isLoading={isLoading}
          emptyTitle={filter === "unassigned" ? "Every vehicle has a rider" : "No vehicles yet"}
          emptyDescription="Add a vehicle to assign it to a rider."
          onPageChange={setPage}
          rowKey={(v) => v.id}
        />
      )}
      <p className="text-xs text-muted-foreground">Reassigning a vehicle or marking it off the road needs backend support.</p>
    </div>
  );
}
