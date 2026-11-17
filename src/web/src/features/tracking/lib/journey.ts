import type { PublicTrackingResult, PublicTrackingStatus } from "@/features/tracking/types";

export const JOURNEY_STAGES = [
  "Received",
  "PickedUp",
  "InTransit",
  "AtDestinationBranch",
  "OutForDelivery",
  "Delivered",
] as const;
export type JourneyStage = (typeof JOURNEY_STAGES)[number];

export type JourneyStepState = "done" | "now" | "next";

export interface JourneyStep {
  stage: JourneyStage;
  label: string;
  state: JourneyStepState;
  /** True only for a failed attempt on the OutForDelivery step. */
  alert: boolean;
  /** ISO of the latest event for this stage, null if none. */
  timestamp: string | null;
  location: string | null;
}

const OFD_INDEX = JOURNEY_STAGES.indexOf("OutForDelivery");

const STATIC_LABELS: Record<JourneyStage, string> = {
  Received: "Order received",
  PickedUp: "Picked up from seller",
  InTransit: "In transit",
  AtDestinationBranch: "Arrived at local branch",
  OutForDelivery: "Out for delivery",
  Delivered: "Delivered",
};

function latestEvent(result: PublicTrackingResult, status: PublicTrackingStatus) {
  const matches = result.events.filter((e) => e.status === status);
  return matches.length > 0 ? matches[matches.length - 1] : null;
}

// Terminal off-route statuses: no "now"/"next" — only the stages actually reached, all done.
function currentIndex(status: PublicTrackingStatus): number | null {
  if (status === "Cancelled" || status === "ReturningToSender") return null;
  if (status === "DeliveryAttemptUnsuccessful") return OFD_INDEX;
  return JOURNEY_STAGES.indexOf(status);
}

export function buildJourney(result: PublicTrackingResult): JourneyStep[] {
  const current = currentIndex(result.currentStatus);
  const failed = result.currentStatus === "DeliveryAttemptUnsuccessful";

  const steps = JOURNEY_STAGES.map((stage, index): JourneyStep => {
    const alert = failed && index === OFD_INDEX;
    const event = latestEvent(result, alert ? "DeliveryAttemptUnsuccessful" : stage);
    const location = event?.location ?? null;

    let state: JourneyStepState;
    if (current === null) state = "done";
    else if (index < current) state = "done";
    else if (index === current) state = stage === "Delivered" ? "done" : "now";
    else state = "next";

    let label = STATIC_LABELS[stage];
    if (alert) label = "Delivery attempt unsuccessful";
    else if (stage === "AtDestinationBranch" && location) label = `Arrived at ${location}`;

    return { stage, label, state, alert, timestamp: event?.timestamp ?? null, location };
  });

  if (current === null) {
    return steps.filter((s) => latestEvent(result, s.stage) !== null);
  }
  return steps;
}

export function formatEventTime(iso: string): string {
  return new Intl.DateTimeFormat("en-GB", {
    weekday: "short",
    day: "numeric",
    month: "short",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(iso));
}
