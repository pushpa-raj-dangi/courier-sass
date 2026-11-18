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
import { formatDate } from "@/features/cod/lib/codFormat";
import { relativeDayLabel } from "@/lib/dates/kathmandu";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantPickups } from "@/features/merchant-pickups/hooks/useMerchantPickups";
import { useMerchantPickupPanelStore } from "@/features/merchant-pickups/store/merchantPickupPanelStore";
import { isOpenStage, parcelsCell, pickupTiles, splitPickups, type PickupTab } from "@/features/merchant-pickups/lib/pickupView";
import type { PickupRequest } from "@/features/merchant-pickups/types";

// requestedForDate is a plain yyyy-MM-dd; anchor at local noon so formatting never shifts the day.
const dayLabel = (isoDate: string) => formatDate(`${isoDate}T12:00:00`);

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
        description="Ask a branch to collect your parcels, and see what has been collected."
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
          note={tiles?.next ? `${tiles.next.branchName ?? "A branch"} · ${tiles.next.parcels} parcels` : undefined}
        />
        <KpiTile
          label="Waiting for a branch"
          value={tiles ? tiles.waiting : "—"}
          note="Requested, no branch confirmation yet"
        />
        <KpiTile
          label="Collected this month"
          value={tiles ? tiles.collectedThisMonth.pickups : "—"}
          note={tiles ? `${tiles.collectedThisMonth.parcels} parcels` : undefined}
        />
      </div>

      <Tabs value={tab} onValueChange={(v) => selectTab(v as PickupTab)}>
        <TabsList>
          <TabsTrigger value="upcoming">Upcoming ({split ? split.upcoming.length : "—"})</TabsTrigger>
          <TabsTrigger value="past">Past ({split ? split.past.length : "—"})</TabsTrigger>
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
                  <div className="font-medium">{dayLabel(p.requestedForDate)}</div>
                  {isOpenStage(p.stage) && (
                    <div className="text-2xs text-muted-foreground">{relativeDayLabel(p.requestedForDate)}</div>
                  )}
                </TableCell>
                <TableCell>{p.branchName ?? "—"}</TableCell>
                <TableCell>
                  <div>{cell.count} {cell.count === 1 ? "parcel" : "parcels"}</div>
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
