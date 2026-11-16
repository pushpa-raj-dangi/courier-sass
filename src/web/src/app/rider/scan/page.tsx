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
}

const MAX_RECENT = 10;

export default function RiderScanPage() {
  const { data: rider } = useMyRider();
  const today = useMyToday(!!rider);
  const pickupScan = useRiderPickupScan(rider?.id ?? "");
  const openRecordAttempt = useRiderAppPanelStore((s) => s.openRecordAttempt);
  const { pendingCount, pendingItems, stuckItems } = useOutboxStatus();
  const [scans, setScans] = useState<ScanResult[]>([]);

  const groups = today.data ? groupRunTasks(today.data.tasks) : undefined;
  const header = groups
    ? `${today.data?.branchName ?? "Your run"} · ${groups.toDeliver.length} to deliver · ${groups.pickups.length} ${groups.pickups.length === 1 ? "pickup" : "pickups"}`
    : "—";

  function handleDetect(awb: string) {
    const match = today.data?.tasks.find((t) => t.awb === awb);
    const kind: ScanKind = !match ? "unknown" : match.status !== "Pending" ? "done" : match.type === "Pickup" ? "pickup" : "delivery";
    setScans((prev) => [{ id: Date.now() + Math.random(), awb, kind, task: match }, ...prev].slice(0, MAX_RECENT));
    if (!match || kind === "done") return;
    if (kind === "pickup") {
      pickupScan.mutate(
        { awb: match.awb, shipmentId: match.shipmentId },
        { onSuccess: () => void useOutboxStatusStore.getState().refresh() },
      );
    } else {
      openRecordAttempt(match);
    }
  }

  function statusWord(scan: ScanResult): { text: string; tone: string } {
    if (scan.kind === "unknown") return { text: "Not on run", tone: "text-destructive" };
    if (scan.kind === "done") return { text: "Already done", tone: "text-muted-foreground" };
    if (scan.kind === "delivery") return { text: "Opened delivery", tone: "text-primary" };
    const state = scan.task ? taskSyncState(scan.task.shipmentId, pendingItems, stuckItems) : "synced";
    if (state === "waiting") return { text: "Waiting to sync", tone: "text-status-ndr" };
    if (state === "needs-review") return { text: "Needs review", tone: "text-destructive" };
    return { text: "Synced", tone: "text-status-delivered" };
  }

  const last = scans[0];

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
        {last && (last.kind === "unknown" || last.kind === "done") ? (
          <section aria-label="Last scan" className="flex items-start gap-2.5 rounded-xl border border-border bg-card p-4">
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
          <section aria-label="Last scan" className="space-y-1.5 rounded-xl border-2 border-status-delivered bg-card p-4">
            <p className="font-mono text-2xs uppercase tracking-[0.1em] text-status-delivered">
              {last.kind === "pickup" ? "PICKED UP" : "OPENED DELIVERY"}
            </p>
            <p className="font-mono text-base font-medium">{last.awb}</p>
            <p className="text-sm text-muted-foreground">
              {`${last.kind === "pickup" ? "Pickup from" : "Delivery to"} ${last.task.recipientName}, ${last.task.recipientAddressLine}`}
            </p>
            {last.kind === "pickup" && (
              <p className={cn("text-xs font-semibold", statusWord(last).tone)}>
                {statusWord(last).text === "Waiting to sync" ? "Saved on this phone · waiting to sync" : statusWord(last).text}
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
              const status = statusWord(scan);
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
