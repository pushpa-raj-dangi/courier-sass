"use client";

import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { RiderPodSheet } from "@/features/rider-app/components/RiderPodSheet";

export function RiderSheets() {
  const { data: rider } = useMyRider();
  if (!rider) return null;
  return <RiderPodSheet riderId={rider.id} />;
}
