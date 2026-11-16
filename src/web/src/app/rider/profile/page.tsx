"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { useSession } from "@/features/auth/hooks/useSession";
import { formatRs } from "@/features/cod/lib/codFormat";
import { useMyToday } from "@/features/rider-app/hooks/useMyToday";
import { hasStaffPermissions } from "@/features/rider-app/lib/runModel";
import { updateOutboxItem } from "@/features/rider-app/sync/outboxDb";
import { drainOutbox } from "@/features/rider-app/sync/syncManager";
import type { OutboxItem } from "@/features/rider-app/sync/types";
import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { riderInitials, roleLabel } from "@/features/riders/lib/riderModel";
import { formatAge } from "@/lib/dates/age";

const CARD = "rounded-xl border border-border bg-card p-4";
const ACTION_LABEL: Record<OutboxItem["action"]["kind"], string> = {
  "pickup-scan": "Pickup",
  "delivery-complete": "Delivery",
  "delivery-fail": "Failed attempt",
};

export default function RiderProfilePage() {
  const { data: rider, isLoading: riderLoading } = useMyRider();
  const today = useMyToday(!!rider);
  const { data: session } = useSession();
  const { pendingCount, stuckItems, refresh } = useOutboxStatus();
  const router = useRouter();

  async function handleLogout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
  }

  async function handleRetry(itemId: string) {
    // Flip the item back to "pending" and kick a drain. A still-permanent 4xx marks it "stuck"
    // again — this only guarantees a rejected action always has a way back (CLAUDE.md §39).
    await updateOutboxItem(itemId, { status: "pending", lastError: undefined });
    await refresh();
    void drainOutbox();
  }

  function awbFor(item: OutboxItem): string {
    const fromRun = today.data?.tasks.find((t) => t.shipmentId === item.action.shipmentId)?.awb;
    if (fromRun) return fromRun;
    if (item.action.kind === "pickup-scan") return item.action.awb;
    return item.action.shipmentId.slice(0, 8);
  }

  const name = session?.fullName ?? "";
  const data = today.data;

  return (
    <div className="flex min-h-full flex-col">
      <header className="flex items-center gap-3.5 bg-sidebar px-4 pb-4 pt-5 text-sidebar-foreground">
        <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-sidebar-primary text-base font-semibold text-sidebar-primary-foreground" aria-hidden="true">
          {name ? riderInitials(name) : "?"}
        </span>
        <div className="space-y-0.5">
          <h1 className="font-display text-xl font-semibold">{name || "Rider"}</h1>
          <p className="text-xs text-sidebar-muted">
            {rider ? `${roleLabel(rider.roleType)}${data?.branchName ? ` · ${data.branchName}` : ""}` : "—"}
          </p>
        </div>
      </header>

      <div className="flex-1 space-y-3 px-4 pb-24 pt-3">
        {today.isError ? (
          <ErrorState title="Couldn't load today" description="Check your connection and try again." onRetry={() => today.refetch()} />
        ) : (
          <section aria-label="Today" className={`${CARD} grid grid-cols-2 gap-3`}>
            {riderLoading || today.isLoading || !data ? (
              <Skeleton role="status" aria-label="Loading today" className="col-span-2 h-16 w-full" />
            ) : (
              <>
                <div className="space-y-1">
                  <p className="font-mono text-2xs uppercase tracking-[0.1em] text-muted-foreground">Today</p>
                  {data.run ? (
                    <>
                      <p className="text-2xl font-semibold tabular-nums">
                        {data.counts.completed}
                        <span className="ml-1 text-sm font-normal text-muted-foreground">{`of ${data.counts.assigned} done`}</span>
                      </p>
                      {data.counts.failed > 0 && <p className="text-xs font-semibold text-destructive">{`${data.counts.failed} failed`}</p>}
                    </>
                  ) : (
                    <p className="text-sm text-muted-foreground">No run today</p>
                  )}
                </div>
                <div className="space-y-1">
                  <p className="font-mono text-2xs uppercase tracking-[0.1em] text-muted-foreground">Cash in hand</p>
                  {data.cash ? (
                    <>
                      <p className="text-2xl font-semibold tabular-nums">{formatRs(data.cash.amount)}</p>
                      <p className="text-xs text-muted-foreground">{`${data.cash.parcelCount} COD ${data.cash.parcelCount === 1 ? "parcel" : "parcels"} · oldest ${formatAge(data.cash.oldestCollectedAt)}`}</p>
                    </>
                  ) : (
                    <p className="text-sm text-muted-foreground">No cash held</p>
                  )}
                </div>
                {data.cash && (
                  <p className="col-span-2 text-xs text-foreground">
                    {`Hand cash over at the ${data.branchName ?? "branch"}${data.branchName ? " branch" : ""} COD counter before you sign off.`}
                  </p>
                )}
              </>
            )}
          </section>
        )}

        {stuckItems.length > 0 && (
          <section aria-label="Needs review" className="space-y-2.5 rounded-xl border-2 border-destructive bg-card p-4">
            <div className="flex items-baseline justify-between">
              <h2 className="text-base font-semibold text-destructive">{`Needs review · ${stuckItems.length}`}</h2>
              <span className="text-xs text-muted-foreground">Not sent to the office</span>
            </div>
            {stuckItems.map((item) => (
              <div key={item.id} className="space-y-1.5 rounded-lg border border-destructive/40 p-3">
                <p className="text-sm font-semibold">
                  {`${ACTION_LABEL[item.action.kind]} · `}
                  <span className="font-mono font-medium">{awbFor(item)}</span>
                </p>
                {item.lastError && <p className="text-sm text-foreground">{item.lastError}</p>}
                <Button type="button" variant="outline" className="h-11" onClick={() => void handleRetry(item.id)}>
                  Try again
                </Button>
              </div>
            ))}
          </section>
        )}

        <section aria-label="Sync" className={`${CARD} space-y-2`}>
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold">Sync</h2>
            <span className={pendingCount > 0 ? "text-xs font-semibold text-status-ndr" : "text-xs font-semibold text-status-delivered"}>
              {pendingCount > 0 ? `${pendingCount} waiting` : "All changes sent"}
            </span>
          </div>
          <p className="text-sm text-muted-foreground">Scans and deliveries are saved on this phone first and sent when you&apos;re online.</p>
          <Button type="button" variant="outline" className="h-11" onClick={() => void drainOutbox()}>
            Send now
          </Button>
        </section>

        <section aria-label="Details" className={CARD}>
          <dl className="grid grid-cols-2 gap-3">
            <div className="space-y-0.5">
              <dt className="text-2xs text-muted-foreground">Base branch</dt>
              <dd className="text-sm font-medium">{data?.branchName ?? "—"}</dd>
            </div>
            <div className="space-y-0.5">
              <dt className="text-2xs text-muted-foreground">Vehicle</dt>
              <dd className="font-mono text-sm font-medium">{data ? data.vehiclePlate ?? "No vehicle" : "—"}</dd>
            </div>
            <div className="space-y-0.5">
              <dt className="text-2xs text-muted-foreground">Licence</dt>
              <dd className="text-sm font-medium">{rider?.licenseNumber ?? "—"}</dd>
            </div>
            <div className="space-y-0.5">
              <dt className="text-2xs text-muted-foreground">Location</dt>
              <dd className="text-sm font-medium">Shared while open</dd>
            </div>
          </dl>
        </section>

        <div className="space-y-2 pt-1">
          {session && hasStaffPermissions(session.permissions) && (
            <Button asChild variant="outline" size="lg" className="h-12 w-full">
              <Link href="/dashboard">Open staff console</Link>
            </Button>
          )}
          <Button type="button" variant="outline" size="lg" className="h-12 w-full text-destructive" onClick={handleLogout}>
            Sign out
          </Button>
        </div>
      </div>
    </div>
  );
}
