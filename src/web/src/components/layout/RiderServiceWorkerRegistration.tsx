"use client";

import { useEffect } from "react";
import { startSyncManager } from "@/features/rider-app/sync/syncManager";

export function RiderServiceWorkerRegistration() {
  useEffect(() => {
    if ("serviceWorker" in navigator) {
      navigator.serviceWorker.register("/sw-rider.js", { scope: "/rider/" }).catch(() => {});
    }
    return startSyncManager();
  }, []);

  return null;
}
