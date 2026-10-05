export const PROGRESS_STEPS = [
  { key: "booked", label: "Booked" },
  { key: "picked-up", label: "Picked up" },
  { key: "origin", label: "Origin hub" },
  { key: "in-transit", label: "In transit" },
  { key: "destination", label: "Destination" },
  { key: "out-for-delivery", label: "Out for delivery" },
  { key: "delivered", label: "Delivered" },
] as const;

export type StepState = "complete" | "current" | "upcoming";
export type ProgressException = "ndr" | "rto" | "cancelled" | null;

// Index of the forward step a status sits at. International statuses ride the in-transit step.
const STEP_INDEX: Record<string, number> = {
  Created: 0, PickupAssigned: 0, PickedUp: 1, AtOriginBranch: 2, Sorted: 2, InTransit: 3,
  CustomsPrepared: 3, HandedToCarrier: 3, InInternationalTransit: 3, CustomsHold: 3,
  InternationalException: 3, CustomsClearance: 3, ReceivedFromCarrier: 3, DeliveredAbroad: 6,
  AtDestinationBranch: 4, OutForDelivery: 5, Delivered: 6,
};

export function buildProgress(status: string): {
  steps: { key: string; label: string; state: StepState }[];
  exception: ProgressException;
  exceptionAfter: number;
} {
  let exception: ProgressException = null;
  let reached: number;
  if (status === "DeliveryFailed" || status === "NdrCreated") {
    exception = "ndr";
    reached = 5;
  } else if (status.startsWith("Rto")) {
    exception = "rto";
    reached = 5;
  } else if (status === "Cancelled") {
    exception = "cancelled";
    reached = 0;
  } else {
    reached = STEP_INDEX[status] ?? 3;
  }

  const delivered = exception === null && reached === 6;
  const steps = PROGRESS_STEPS.map((step, i) => {
    let state: StepState;
    if (delivered || i < reached) state = "complete";
    else if (i === reached && exception === null) state = "current";
    else if (i === reached && exception !== null) state = "complete";
    else state = "upcoming";
    return { key: step.key, label: step.label, state };
  });
  return { steps, exception, exceptionAfter: exception === null ? -1 : reached };
}
