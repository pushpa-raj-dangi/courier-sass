"use client";

import { Plus } from "lucide-react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/page/PageHeader";
import { KpiTile } from "@/components/page/KpiTile";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { LoadingState } from "@/components/states/LoadingState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { BorderedTableContainer } from "@/components/data-table/BorderedTableContainer";
import { relativeDayLabel } from "@/lib/dates/kathmandu";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantPickups } from "@/features/merchant-pickups/hooks/useMerchantPickups";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { isOpenStage, parcelsCell, parcelsLabel, pickupDayLabel, pickupTiles, splitPickups, type PickupTab } from "@/features/merchant-pickups/lib/pickupView";
import type { PickupRequest } from "@/features/merchant-pickups/types";

const dayLabel = pickupDayLabel;

export function MerchantPickupsPage() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const tab: PickupTab = searchParams.get("tab") === "past" ? "past" : "upcoming";

  const { data: pickups, isLoading, isError, refetch } = useMerchantPickups();
  const canCreate = useMerchantPermission("merchant.pickups.create");
  const openCreate = useMerchantPickupPanelStore((s) => s.openCreate);
  const openDetail = useMerchantPickupPanelStore((s) => s.openDetail);

  function selectTab(next: PickupTab) {
    router.replace(next === "past" ? `${pathname}?tab=past` : pathname, { scroll: false });
  }

  const split = pickups ? splitPickups(pickups) : null;
  const tiles = pickups ? pickupTiles(pickups) : null;
  const rows = split ? split[tab] : [];

  return (
    <div className="space-y-5">
      <PageHeader
        title="Pickups"
        description="When a rider comes to collect your parcels, and which ones"
        actions={
          canCreate && (
            <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
              <Plus className="h-4 w-4" strokeWidth={2} />
              Request pickup
            </Button>
          )
        }
      />

      <div className="grid gap-3 sm:grid-cols-3">
        <KpiTile
          label="Next pickup"
          value={tiles ? (tiles.next ? dayLabel(tiles.next.date) : "None booked") : "—"}
          note={tiles?.next ? `${tiles.next.branchName ?? "A branch"} · ${parcelsLabel(tiles.next.parcels)}` : undefined}
        />
        <KpiTile
          label="Waiting to be scheduled"
          value={tiles ? tiles.waiting : "—"}
          note="requests the branch has not confirmed"
        />
        <KpiTile
          label="Collected this month"
          value={tiles ? tiles.collectedThisMonth.pickups : "—"}
          note={tiles ? `pickups · ${parcelsLabel(tiles.collectedThisMonth.parcels)}` : undefined}
        />
      </div>

      <Tabs value={tab} onValueChange={(v) => selectTab(v as PickupTab)}>
        <TabsList>
          <TabsTrigger value="upcoming" className="gap-1.5">
            Upcoming <span className="tabular-nums text-muted-foreground">{split ? split.upcoming.length : "—"}</span>
          </TabsTrigger>
          <TabsTrigger value="past" className="gap-1.5">
            Past <span className="tabular-nums text-muted-foreground">{split ? split.past.length : "—"}</span>
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {isLoading && <LoadingState rows={4} />}
      {isError && (
        <ErrorState
          title="Couldn't load pickup requests"
          description="Something went wrong fetching your pickup requests."
          onRetry={() => refetch()}
        />
      )}
      {split && rows.length === 0 && (
        <EmptyState
          title={tab === "upcoming" ? "No pickups coming up" : "No past pickups yet"}
          description={
            tab === "upcoming"
              ? "Request a pickup once you have parcels ready for a branch to collect."
              : "Collected and cancelled pickups show up here."
          }
        />
      )}
      {split && rows.length > 0 && <PickupsTable rows={rows} onOpen={openDetail} />}
    </div>
  );
}

function PickupsTable({ rows, onOpen }: { rows: PickupRequest[]; onOpen: (id: string) => void }) {
  return (
    <BorderedTableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Pickup date</TableHead>
            <TableHead>Collecting branch</TableHead>
            <TableHead>Parcels</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Notes</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((p) => {
            const cell = parcelsCell(p);
            return (
              <TableRow key={p.id} className="cursor-pointer" onClick={() => onOpen(p.id)}>
                <TableCell>
                  <Button
                    type="button"
                    variant="link"
                    className="h-auto p-0 font-medium text-foreground"
                    onClick={(e) => {
                      e.stopPropagation();
                      onOpen(p.id);
                    }}
                  >
                    {dayLabel(p.requestedForDate)}
                  </Button>
                  {isOpenStage(p.stage) && (
                    <div className="text-2xs text-muted-foreground">{relativeDayLabel(p.requestedForDate)}</div>
                  )}
                </TableCell>
                <TableCell>{p.branchName ?? "—"}</TableCell>
                <TableCell>
                  <div>{parcelsLabel(cell.count)}</div>
                  {cell.count > 0 && (
                    <div className="font-mono text-2xs text-muted-foreground">
                      {cell.awbs.join(", ")}
                      {cell.more > 0 && ` +${cell.more}`}
                    </div>
                  )}
                </TableCell>
                <TableCell><StatusBadge status={p.stage} namespace="PickupStage" /></TableCell>
                <TableCell>{p.notes ?? "—"}</TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </BorderedTableContainer>
  );
}
