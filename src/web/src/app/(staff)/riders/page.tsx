"use client";

import { Plus, Bike, CheckCircle2, XCircle, ClipboardCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { SummaryStatsBar, type StatTile } from "@/components/summary-stats/SummaryStatsBar";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useVehicles } from "@/features/riders/hooks/useVehicles";
import { useRidersSummary, type RiderSummary } from "@/features/riders/hooks/useRidersSummary";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import type { Rider, Vehicle } from "@/features/riders/types";

function buildTiles(summary: RiderSummary): StatTile[] {
  return [
    { label: "Total Riders", value: summary.total.toLocaleString(), icon: Bike, badgeClass: "bg-muted text-muted-foreground" },
    { label: "Active", value: summary.active.toLocaleString(), icon: CheckCircle2, badgeClass: "bg-status-delivered/15 text-status-delivered", highlight: true },
    { label: "Inactive", value: summary.inactive.toLocaleString(), icon: XCircle, badgeClass: "bg-status-cancelled/15 text-status-cancelled" },
    { label: "Currently Assigned", value: summary.currentlyAssigned.toLocaleString(), icon: ClipboardCheck, badgeClass: "bg-status-out-for-delivery/15 text-status-out-for-delivery" },
  ];
}

export default function RidersPage() {
  const { data, isLoading, isError, refetch } = useRiders();
  const summary = useRidersSummary();
  const canCreate = usePermission("rider.create");
  const openCreateRider = useRiderPanelStore((s) => s.openCreateRider);
  const { data: vehicles, isLoading: vehiclesLoading } = useVehicles();
  const openCreateVehicle = useRiderPanelStore((s) => s.openCreateVehicle);
  const canAssign = usePermission("delivery.assign");
  const openAssignTask = useRiderPanelStore((s) => s.openAssignTask);

  const columns: ColumnDef<Rider>[] = [
    { key: "licenseNumber", header: "License Number", render: (r) => r.licenseNumber },
    { key: "roleType", header: "Role", render: (r) => r.roleType },
    { key: "active", header: "Active", render: (r) => (r.isActive ? "Yes" : "No") },
  ];

  const riderLicenseById = new Map((data ?? []).map((r) => [r.id, r.licenseNumber]));

  const vehicleColumns: ColumnDef<Vehicle>[] = [
    { key: "plateNumber", header: "Plate Number", render: (v) => v.plateNumber },
    { key: "type", header: "Type", render: (v) => v.type },
    {
      key: "riderId",
      header: "Assigned Rider",
      render: (v) => (v.riderId ? (riderLicenseById.get(v.riderId) ?? v.riderId) : "Unassigned"),
    },
  ];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Riders</h1>
        <div className="flex items-center gap-2">
          {canAssign && (
            <Button size="sm" variant="outline" className="gap-1.5" onClick={() => openAssignTask()}>
              Assign Task
            </Button>
          )}
          {canCreate && (
            <Button size="sm" className="gap-1.5" onClick={() => openCreateRider()}>
              <Plus className="h-4 w-4" strokeWidth={2} />
              New Rider
            </Button>
          )}
        </div>
      </div>
      <SummaryStatsBar
        tiles={summary.data ? buildTiles(summary.data) : []}
        isLoading={summary.isLoading}
        isError={summary.isError}
        skeletonCount={4}
      />
      {isLoading && (
        <div className="flex items-center justify-center p-12">
          <Spinner className="h-6 w-6" />
        </div>
      )}
      {!isLoading && isError && (
        <ErrorState
          title="Couldn't load riders"
          description="Something went wrong fetching the rider list."
          onRetry={() => refetch()}
        />
      )}
      {!isLoading && !isError && (
        <>
          <DataTable
            columns={columns}
            data={
              data && {
                items: data,
                page: 1,
                pageSize: data.length,
                totalCount: data.length,
                totalPages: 1,
              }
            }
            isLoading={false}
            emptyTitle="No riders yet"
            emptyDescription="Create your first rider to start assigning pickups and deliveries."
            onPageChange={() => {}}
            rowKey={(r) => r.id}
          />
          <div className="flex items-center justify-between pt-4">
            <h2 className="font-display text-lg font-semibold">Vehicles</h2>
            {canCreate && (
              <Button size="sm" variant="outline" className="gap-1.5" onClick={() => openCreateVehicle()}>
                <Plus className="h-4 w-4" strokeWidth={2} />
                New Vehicle
              </Button>
            )}
          </div>
          <DataTable
            columns={vehicleColumns}
            data={
              vehicles && {
                items: vehicles,
                page: 1,
                pageSize: vehicles.length,
                totalCount: vehicles.length,
                totalPages: 1,
              }
            }
            isLoading={vehiclesLoading}
            emptyTitle="No vehicles yet"
            emptyDescription="Add a vehicle to assign it to a rider."
            onPageChange={() => {}}
            rowKey={(v) => v.id}
          />
        </>
      )}
    </div>
  );
}
