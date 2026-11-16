"use client";

import { useState } from "react";
import { AlertTriangle } from "lucide-react";
import { BarcodeScanner } from "@/components/scanner/BarcodeScanner";
import { useMyToday } from "@/features/rider-app/hooks/useMyToday";
import { useRiderPickupScan } from "@/features/rider-app/hooks/useRiderPickupScan";
import { groupRunTasks, taskSyncState } from "@/features/rider-app/lib/runModel";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";
import { useOutboxStatusStore } from "@/features/rider-app/sync/outboxStatusStore";
import { useOutboxStatus } from "@/features/rider-app/sync/useOutboxStatus";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import type { MyTodayTask } from "@/features/riders/types";
import { cn } from "@/lib/utils";

type ScanKind = "pickup" | "delivery" | "done" | "unknown";
interface ScanResult {
  id: number;
  awb: string;
  kind: ScanKind;
  task?: MyTodayTask;
  save?: SaveState;
}

type SaveState = "saving" | "queued" | "failed";
type StatusKey = "not-on-run" | "done" | "opened" | "saving" | "failed" | "waiting" | "needs-review" | "synced";

const STATUS_COPY: Record<StatusKey, { text: string; tone: string }> = {
  "not-on-run": { text: "Not on run", tone: "text-destructive" },
  done: { text: "Already done", tone: "text-muted-foreground" },
  opened: { text: "Opened delivery", tone: "text-primary" },
  saving: { text: "Saving…", tone: "text-muted-foreground" },
  failed: { text: "Couldn't save · scan again", tone: "text-destructive" },
  waiting: { text: "Waiting to sync", tone: "text-status-ndr" },
  "needs-review": { text: "Needs review", tone: "text-destructive" },
  synced: { text: "Synced", tone: "text-status-delivered" },
};

const LAST_SCAN_LINE: Partial<Record<StatusKey, string>> = {
  waiting: "Saved on this phone · waiting to sync",
  failed: "Couldn't save this scan · scan again",
};

const MAX_RECENT = 10;

export default function RiderScanPage() {
  const { data: rider } = useMyRider();
  const today = useMyToday(!!rider);
  const pickupScan = useRiderPickupScan(rider?.id ?? "");
  const openRecordAttempt = useRiderAppPanelStore((s) => s.openRecordAttempt);
  const { pendingCount, pendingItems, stuckItems } = useOutboxStatus();
  const [scans, setScans] = useState<ScanResult[]>([]);
  const [notice, setNotice] = useState<string | null>(null);

  const groups = today.data ? groupRunTasks(today.data.tasks) : undefined;
  const header = groups
    ? `${today.data?.branchName ?? "Your run"} · ${groups.toDeliver.length} to deliver · ${groups.pickups.length} ${groups.pickups.length === 1 ? "pickup" : "pickups"}`
    : "—";

  function updateScan(id: number, save: SaveState) {
    setScans((prev) => prev.map((s) => (s.id === id ? { ...s, save } : s)));
  }

  function handleDetect(awb: string) {
    if (!today.data) {
      setNotice(
        today.isError
          ? "Couldn't load your run — scan again when you're back online."
          : "Your run is still loading — scan again in a moment.",
      );
      return;
    }
    setNotice(null);
    const match = today.data.tasks.find((t) => t.awb === awb);
    const kind: ScanKind = !match ? "unknown" : match.status !== "Pending" ? "done" : match.type === "Pickup" ? "pickup" : "delivery";
    const id = Date.now() + Math.random();
    const alreadyQueued =
      !!match &&
      kind === "pickup" &&
      (taskSyncState(match.shipmentId, pendingItems, stuckItems) !== "synced" ||
        scans.some((s) => s.task?.shipmentId === match.shipmentId && (s.save === "saving" || s.save === "queued")));
    const save: SaveState | undefined = kind === "pickup" ? (alreadyQueued ? "queued" : "saving") : undefined;
    setScans((prev) => [{ id, awb, kind, task: match, save }, ...prev].slice(0, MAX_RECENT));
    if (!match || kind === "done") return;
    if (kind === "pickup") {
      if (alreadyQueued) return;
      // mutateAsync (not per-call mutate callbacks) so every scan settles, even when scans overlap.
      pickupScan
        .mutateAsync({ awb: match.awb, shipmentId: match.shipmentId })
        .then(() => {
          updateScan(id, "queued");
          void useOutboxStatusStore.getState().refresh();
        })
        .catch(() => updateScan(id, "failed"));
    } else {
      openRecordAttempt(match);
    }
  }

  function statusKey(scan: ScanResult): StatusKey {
    if (scan.kind === "unknown") return "not-on-run";
    if (scan.kind === "done") return "done";
    if (scan.kind === "delivery") return "opened";
    if (scan.save === "saving") return "saving";
    if (scan.save === "failed") return "failed";
    const state = scan.task ? taskSyncState(scan.task.shipmentId, pendingItems, stuckItems) : "synced";
    if (state === "needs-review") return "needs-review";
    if (state === "waiting") return "waiting";
    // Queued but not (yet) visible in the outbox: only the run data can prove it reached the server.
    const live = today.data?.tasks.find((t) => t.shipmentId === scan.task?.shipmentId);
    return live && live.status !== "Pending" ? "synced" : "waiting";
  }

  const last = scans[0];
  const lastKey = last ? statusKey(last) : undefined;
  const lastBad = last?.kind === "pickup" && (lastKey === "failed" || lastKey === "needs-review");

  return (
    <div className="flex min-h-full flex-col">
      <div className="space-y-3 bg-sidebar px-4 pb-4 pt-5 text-sidebar-foreground">
        <div className="space-y-0.5">
          <p className="text-xs text-sidebar-muted">{header}</p>
          <h1 className="font-display text-xl font-semibold">Scan parcel</h1>
        </div>
        <BarcodeScanner onDetect={handleDetect} />
      </div>

      <div className="flex-1 space-y-2.5 px-4 pb-24 pt-3">
        {notice ? (
          <section role="status" aria-label="Last scan" className="flex items-start gap-2.5 rounded-xl border border-border bg-card p-4">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-status-ndr" aria-hidden="true" />
            <p className="text-sm font-semibold">{notice}</p>
          </section>
        ) : last && (last.kind === "unknown" || last.kind === "done") ? (
          <section role="status" aria-label="Last scan" className="flex items-start gap-2.5 rounded-xl border border-border bg-card p-4">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
            <div className="space-y-0.5">
              <p className="text-sm font-semibold">
                {last.kind === "done" ? `${last.awb} is already done today` : `${last.awb} isn't on your run today`}
              </p>
              <p className="text-xs text-muted-foreground">
                {last.kind === "done" ? "Nothing to do for this parcel." : "Hand it to the branch counter, or ask dispatch to assign it to you."}
              </p>
            </div>
          </section>
        ) : last && last.task ? (
          <section
            role="status"
            aria-label="Last scan"
            className={cn("space-y-1.5 rounded-xl border-2 bg-card p-4", lastBad ? "border-destructive" : "border-status-delivered")}
          >
            <p className={cn("font-mono text-2xs uppercase tracking-[0.1em]", lastBad ? "text-destructive" : "text-status-delivered")}>
              {last.kind !== "pickup" ? "OPENED DELIVERY" : lastKey === "failed" ? "NOT SAVED" : lastKey === "needs-review" ? "NEEDS REVIEW" : "PICKED UP"}
            </p>
            <p className="font-mono text-base font-medium">{last.awb}</p>
            <p className="text-sm text-muted-foreground">
              {`${last.kind === "pickup" ? "Pickup from" : "Delivery to"} ${last.task.recipientName}, ${last.task.recipientAddressLine}`}
            </p>
            {last.kind === "pickup" && (
              <p className={cn("text-xs font-semibold", STATUS_COPY[statusKey(last)].tone)}>
                {LAST_SCAN_LINE[statusKey(last)] ?? STATUS_COPY[statusKey(last)].text}
              </p>
            )}
          </section>
        ) : (
          <p className="text-sm text-muted-foreground">Scan a barcode or type an AWB to start.</p>
        )}

        {scans.length > 0 && (
          <section aria-label="Recent scans" className="space-y-2">
            <h2 className="font-mono text-2xs uppercase tracking-[0.1em] text-muted-foreground">Recent scans</h2>
            {scans.map((scan) => {
              const status = STATUS_COPY[statusKey(scan)];
              return (
                <div key={scan.id} className="flex items-center justify-between gap-3 rounded-xl border border-border bg-card px-4 py-2.5">
                  <div className="min-w-0">
                    <p className="font-mono text-sm">{scan.awb}</p>
                    <p className="truncate text-xs text-muted-foreground">
                      {scan.task ? `${scan.task.type} · ${scan.task.recipientName}` : "Not on your run"}
                    </p>
                  </div>
                  <span className={cn("shrink-0 text-xs font-semibold", status.tone)}>{status.text}</span>
                </div>
              );
            })}
          </section>
        )}
      </div>

      {pendingCount > 0 && (
        <div className="fixed inset-x-0 bottom-16 flex justify-between border-t border-border bg-muted px-4 py-2 text-xs font-medium text-foreground">
          <span>{`${pendingCount} ${pendingCount === 1 ? "scan" : "scans"} waiting to sync`}</span>
          <span className="text-muted-foreground">Sends when you&apos;re online</span>
        </div>
      )}
    </div>
  );
}
