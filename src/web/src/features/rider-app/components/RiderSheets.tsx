"use client";

import { useMyRider } from "@/features/riders/hooks/useMyRider";
import { RiderRecordAttemptSheet } from "@/features/rider-app/components/RiderRecordAttemptSheet";

export function RiderSheets() {
  const { data: rider } = useMyRider();
  if (!rider) return null;
  return <RiderRecordAttemptSheet riderId={rider.id} />;
}
