"use client";

import { useLocationReporting } from "@/features/rider-app/location/useLocationReporting";

// Mounted once in the rider layout (alongside RiderServiceWorkerRegistration) so foreground
// location reporting runs across every rider screen without requiring the server-rendered
// layout itself to become a client component.
export function RiderLocationReporting() {
  useLocationReporting();
  return null;
}
