import type { PublicTrackingResult, PublicTrackingStatus } from "@/features/tracking/types";

interface StatusMeta {
  /** One sentence, written for the recipient waiting on this parcel. */
  summary: string;
  /** Short, human headline for the hero of the result page. */
  headline: string;
}

export const STATUS_META: Record<PublicTrackingStatus, StatusMeta> = {
  Received: {
    summary: "Booked and waiting to be picked up.",
    headline: "Booked, waiting for pickup",
  },
  PickedUp: {
    summary: "Collected and on its way to the origin hub.",
    headline: "Picked up from the seller",
  },
  InTransit: {
    summary: "Moving between hubs toward its destination.",
    headline: "On its way",
  },
  AtDestinationBranch: {
    summary: "Arrived at the destination branch.",
    headline: "At your local branch",
  },
  OutForDelivery: {
    summary: "With a rider, on its way to the recipient.",
    headline: "Arriving today",
  },
  Delivered: { summary: "Delivered.", headline: "Delivered" },
  DeliveryAttemptUnsuccessful: {
    summary: "A delivery attempt was unsuccessful. We'll contact you or try again.",
    headline: "We missed you",
  },
  Cancelled: {
    summary: "This shipment was cancelled.",
    headline: "Shipment cancelled",
  },
  ReturningToSender: {
    summary: "This parcel is on its way back to the seller.",
    headline: "Returning to the seller",
  },
};

/** Statuses whose card shows the summary sentence under the headline. */
export const SHOWS_SUMMARY: ReadonlySet<PublicTrackingStatus> = new Set<PublicTrackingStatus>([
  "DeliveryAttemptUnsuccessful",
  "ReturningToSender",
]);

function isSameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/**
 * The hero headline. "Arriving today" is only promised when the latest out-for-delivery event
 * happened on the viewer's local today — a parcel left out for delivery yesterday reads
 * "Out for delivery" instead.
 */
export function headlineFor(result: PublicTrackingResult, now: Date): string {
  if (result.currentStatus === "OutForDelivery") {
    const ofd = result.events.filter((e) => e.status === "OutForDelivery");
    const latest = ofd[ofd.length - 1];
    if (!latest || !isSameLocalDay(new Date(latest.timestamp), now)) return "Out for delivery";
  }
  return STATUS_META[result.currentStatus].headline;
}
