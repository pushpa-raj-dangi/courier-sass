"use client";

import { useState } from "react";
import { BarcodeScanner } from "@/components/scanner/BarcodeScanner";
import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { useMyTasksToday } from "@/features/riders/hooks/useMyTasksToday";
import { useRiderPickupScan } from "@/features/rider-app/hooks/useRiderPickupScan";
import { useRiderAppPanelStore } from "@/features/rider-app/store/riderAppPanelStore";

export default function RiderScanPage() {
  const { data: rider } = useMyRider();
  const { data: tasksToday } = useMyTasksToday(!!rider);
  const pickupScan = useRiderPickupScan(rider?.id ?? "");
  const [message, setMessage] = useState<string | null>(null);

  function handleDetect(awb: string) {
    const task = tasksToday?.tasks.find((t) => t.awb === awb);
    if (!task) {
      setMessage(`No task found for AWB ${awb}.`);
      return;
    }
    setMessage(null);
    if (task.type === "Pickup") {
      pickupScan.mutate({ awb: task.awb, shipmentId: task.shipmentId });
    } else {
      useRiderAppPanelStore.getState().openRecordAttempt(task);
    }
  }

  return (
    <div className="space-y-4 p-4">
      <h1 className="text-xl font-semibold">Scan</h1>
      {message && <p className="text-sm text-destructive">{message}</p>}
      <BarcodeScanner onDetect={handleDetect} />
    </div>
  );
}
