"use client";

import { useEffect, useRef } from "react";

const REPORT_INTERVAL_MS = 20_000;

// Foreground-only by construction (spec §3, §5.1): watchPosition only runs while the tab is
// visible, and a ping made while offline is simply dropped, never queued — a stale replayed
// position would show a wrong "current" location, which is worse than a missing one. Uses plain
// fetch (not clientFetch/useMutation): this is a fire-and-forget side effect with no UI state to
// react to, and clientFetch's unconditional response.json() call would throw on the backend's
// 204 No Content response.
export function useLocationReporting() {
  const watchIdRef = useRef<number | null>(null);
  const lastSentAtRef = useRef(0);

  useEffect(() => {
    function sendPing(position: GeolocationPosition) {
      const now = Date.now();
      if (now - lastSentAtRef.current < REPORT_INTERVAL_MS) {
        return;
      }
      lastSentAtRef.current = now;
      fetch("/api/riders/me/location", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
          accuracyMeters: position.coords.accuracy,
          recordedAt: new Date(position.timestamp).toISOString(),
        }),
      }).catch(() => {
        // A dropped ping just means a slightly staler position — never surfaced to the rider.
      });
    }

    function startWatch() {
      if (watchIdRef.current !== null || typeof navigator === "undefined" || !("geolocation" in navigator)) {
        return;
      }
      watchIdRef.current = navigator.geolocation.watchPosition(sendPing, () => {}, {
        enableHighAccuracy: false,
        maximumAge: 15_000,
      });
    }

    function stopWatch() {
      if (watchIdRef.current !== null) {
        navigator.geolocation.clearWatch(watchIdRef.current);
        watchIdRef.current = null;
      }
    }

    function handleVisibilityChange() {
      if (document.visibilityState === "visible") {
        startWatch();
      } else {
        stopWatch();
      }
    }

    if (document.visibilityState === "visible") {
      startWatch();
    }
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      stopWatch();
    };
  }, []);
}
