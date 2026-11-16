"use client";

import { Suspense } from "react";
import { Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageHeader } from "@/components/page/PageHeader";
import { PipelineStrip, type PipelineStage } from "@/components/page/PipelineStrip";
import { ErrorState } from "@/components/states/ErrorState";
import { usePermission } from "@/features/auth/hooks/usePermission";
import { useSession } from "@/features/auth/hooks/useSession";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiderCash } from "@/features/cod/hooks/useRiderCash";
import { RidersBoardTab } from "@/features/riders/components/RidersBoardTab";
import { RidersFleetTab } from "@/features/riders/components/RidersFleetTab";
import { RidersMapTab } from "@/features/riders/components/RidersMapTab";
import { useRiderBoard } from "@/features/riders/hooks/useRiderBoard";
import { useVehicles } from "@/features/riders/hooks/useVehicles";
import {
  RIDER_STAGES, cashByRider, filterBoardRows, fleetVehicles, parseFleetFilter, parseRiderRole, parseRiderStage,
  parseRidersTab, riderStageCounts, riderStageLabel, staleOnRunCount,
} from "@/features/riders/lib/riderModel";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import { useSearchParamState } from "@/lib/hooks/useSearchParamState";

const ALL = "all";

function RidersPageContent() {
  const { params, update } = useSearchParamState();
  const tab = parseRidersTab(params.get("tab"));
  const stage = parseRiderStage(params.get("stage"));
  const role = parseRiderRole(params.get("role"));
  const showInactive = params.get("inactive") === "1";
  const fleetFilter = parseFleetFilter(params.get("fleet"));
  const branchId = params.get("branch") ?? undefined;

  const { data: session } = useSession();
  const { data: branches } = useBranches();
  const scopedBranches = (branches ?? []).filter(
    (b) => session?.hasAllBranchesScope || session?.branches.some((s) => s.branchId === b.id),
  );
  const showBranchPicker = Boolean(session?.hasAllBranchesScope) || scopedBranches.length > 1;
  // A one-branch user has no picker; the map shows one branch, so it uses theirs.
  const mapBranchId = branchId ?? (!session?.hasAllBranchesScope && scopedBranches.length === 1 ? scopedBranches[0].id : undefined);
  const branchLabel = branchId ? scopedBranches.find((b) => b.id === branchId)?.name ?? "Selected branch" : "All branches";

  const canMap = usePermission("rider.location.read");
  const canCash = usePermission("cod.read");
  const canAssign = usePermission("delivery.assign");
  const canCreate = usePermission("rider.create");
  const activeTab = tab === "map" && !canMap ? "riders" : tab;

  const board = useRiderBoard(branchId);
  const riderCash = useRiderCash({ branchId }, { enabled: canCash });
  const vehicles = useVehicles();
  const panel = useRiderPanelStore((s) => s.panel);
  const openDetail = useRiderPanelStore((s) => s.openDetail);
  const openCreateRider = useRiderPanelStore((s) => s.openCreateRider);
  const openCreateVehicle = useRiderPanelStore((s) => s.openCreateVehicle);
  const openAssignTask = useRiderPanelStore((s) => s.openAssignTask);

  const now = new Date();
  const rows = board.data?.items;
  const counts = rows && riderStageCounts(rows);
  const staleCount = rows ? staleOnRunCount(rows, now) : 0;
  const shownRows = rows && filterBoardRows(rows, { stage, role, showInactive });
  const cash = riderCash.data ? cashByRider(riderCash.data.items) : undefined;
  const branchPicked = branchId !== undefined;
  const fleet =
    vehicles.data && (!branchPicked || rows)
      ? fleetVehicles(vehicles.data, new Set((rows ?? []).map((r) => r.riderId)), branchPicked)
      : undefined;

  const stages: PipelineStage[] = RIDER_STAGES.map((s) => ({
    key: s.key,
    label: s.label,
    token: s.token,
    count: counts ? counts[s.key] : null,
    caption: s.key === "on" && staleCount > 0 ? `${staleCount} with stale location` : undefined,
  }));

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow="Operations"
        title="Riders"
        description={`${branchLabel} · ${counts ? `${counts.on} out today` : "—"}`}
        actions={
          <>
            {showBranchPicker && (
              <Select value={branchId ?? ALL} onValueChange={(v) => update({ branch: v === ALL ? undefined : v })}>
                <SelectTrigger className="w-48" aria-label="Branch">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL}>All branches</SelectItem>
                  {scopedBranches.map((b) => (
                    <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
            {canAssign && (
              <Button type="button" variant="outline" onClick={() => openAssignTask()}>
                Assign task
              </Button>
            )}
            {canCreate && activeTab === "fleet" && (
              <Button type="button" variant="outline" className="gap-1.5" onClick={() => openCreateVehicle()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                Add vehicle
              </Button>
            )}
            {canCreate && (
              <Button type="button" className="gap-1.5" onClick={() => openCreateRider()}>
                <Plus className="h-4 w-4" aria-hidden="true" />
                Add rider
              </Button>
            )}
          </>
        }
      />

      <section aria-label="Rider day summary" className="space-y-2">
        {board.isLoading && (
          <div role="status" aria-label="Loading rider counts" className="grid grid-cols-3 gap-2.5">
            {RIDER_STAGES.map((s) => (
              <Skeleton key={s.key} className="h-20 rounded-lg" />
            ))}
          </div>
        )}
        {board.isError && (
          <ErrorState title="Couldn't load rider counts" description="Today's rider summary is unavailable right now." onRetry={() => board.refetch()} />
        )}
        {counts && (
          <>
            <PipelineStrip
              ariaLabel="Rider day"
              stages={stages}
              highlightKey={staleCount > 0 ? "on" : undefined}
              selectedKey={stage}
              onSelect={(key) => update({ stage: key === stage ? undefined : key, tab: undefined })}
            />
            <p className="text-2xs text-muted-foreground">Today&apos;s runs (UTC date) · active riders only</p>
          </>
        )}
      </section>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Tabs value={activeTab} onValueChange={(v) => update({ tab: v === "riders" ? undefined : v })}>
          <TabsList>
            <TabsTrigger value="riders">
              Riders
              {shownRows && <span className="ml-1.5 tabular-nums text-muted-foreground">{shownRows.length}</span>}
            </TabsTrigger>
            {canMap && <TabsTrigger value="map">Map</TabsTrigger>}
            <TabsTrigger value="fleet">
              Fleet
              {fleet && <span className="ml-1.5 tabular-nums text-muted-foreground">{fleet.length}</span>}
            </TabsTrigger>
          </TabsList>
        </Tabs>
        {activeTab === "riders" && (
          <div className="flex flex-wrap items-center gap-3">
            <Select value={role ?? ALL} onValueChange={(v) => update({ role: v === ALL ? undefined : v })}>
              <SelectTrigger className="w-40" aria-label="Role">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>All roles</SelectItem>
                <SelectItem value="Pickup">Pickup riders</SelectItem>
                <SelectItem value="Delivery">Delivery riders</SelectItem>
              </SelectContent>
            </Select>
            <div className="flex items-center gap-2">
              <Checkbox
                id="riders-show-inactive"
                checked={showInactive}
                onCheckedChange={(checked) => update({ inactive: checked === true ? "1" : undefined })}
              />
              <Label htmlFor="riders-show-inactive" className="text-sm font-normal">Show inactive</Label>
            </div>
            {stage && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-8 gap-1.5"
                aria-label={`Clear stage filter ${riderStageLabel(stage)}`}
                onClick={() => update({ stage: undefined })}
              >
                {`Stage: ${riderStageLabel(stage)}`}
                <X className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            )}
          </div>
        )}
      </div>

      {activeTab === "riders" && (
        <RidersBoardTab
          rows={shownRows}
          isLoading={board.isLoading}
          isError={board.isError}
          onRetry={() => board.refetch()}
          cash={cash}
          showCash={canCash}
          selectedRiderId={panel.mode === "detail" ? panel.riderId : undefined}
          onOpenRider={openDetail}
          filterKey={`${stage ?? ""}|${role ?? ""}|${showInactive ? 1 : 0}|${branchId ?? ""}`}
          now={now}
        />
      )}
      {activeTab === "map" && (
        <RidersMapTab
          branchId={mapBranchId}
          branch={branches?.find((b) => b.id === mapBranchId)}
          rows={rows}
          cash={cash}
          showCash={canCash}
          onOpenRider={openDetail}
          now={now}
        />
      )}
      {activeTab === "fleet" && (
        <RidersFleetTab
          vehicles={fleet}
          isLoading={vehicles.isLoading || (branchPicked && board.isLoading)}
          isError={vehicles.isError}
          onRetry={() => vehicles.refetch()}
          rows={rows}
          filter={fleetFilter}
          onFilterChange={(f) => update({ fleet: f === "all" ? undefined : f })}
        />
      )}
    </div>
  );
}

export default function RidersPage() {
  return (
    <Suspense>
      <RidersPageContent />
    </Suspense>
  );
}
