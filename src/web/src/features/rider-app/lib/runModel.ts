import type { MyTodayTask } from "@/features/riders/types";
import type { OutboxItem, RecordFailedAttemptFields } from "@/features/rider-app/sync/types";

const bySequence = (a: MyTodayTask, b: MyTodayTask) => a.sequence - b.sequence;

export function nextStop(tasks: MyTodayTask[]): MyTodayTask | undefined {
  return tasks.filter((t) => t.status === "Pending").sort(bySequence)[0];
}

export function groupRunTasks(tasks: MyTodayTask[]): { toDeliver: MyTodayTask[]; pickups: MyTodayTask[]; done: MyTodayTask[] } {
  const sorted = [...tasks].sort(bySequence);
  return {
    toDeliver: sorted.filter((t) => t.status === "Pending" && t.type === "Delivery"),
    pickups: sorted.filter((t) => t.status === "Pending" && t.type === "Pickup"),
    done: sorted.filter((t) => t.status !== "Pending"),
  };
}

export function awbTail(awb: string): string {
  return awb.length <= 5 ? awb : `…${awb.slice(-5)}`;
}

export function mapsHref(addressLine: string, ward: number): string {
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${addressLine}, Ward ${ward}`)}`;
}

export type CashMatch = { kind: "match" | "short" | "over"; difference: number };

export function cashMatch(expected: number, entered: number): CashMatch {
  const difference = Math.round((entered - expected) * 100) / 100;
  if (difference === 0) return { kind: "match", difference: 0 };
  return difference < 0 ? { kind: "short", difference: -difference } : { kind: "over", difference };
}

export type TaskSyncState = "synced" | "waiting" | "needs-review";

/** "waiting" = queued on this phone, not yet accepted by the server; "needs-review" = rejected. */
export function taskSyncState(shipmentId: string, pendingItems: OutboxItem[], stuckItems: OutboxItem[]): TaskSyncState {
  if (stuckItems.some((i) => i.action.shipmentId === shipmentId)) return "needs-review";
  if (pendingItems.some((i) => i.action.shipmentId === shipmentId)) return "waiting";
  return "synced";
}

// The seeded DeliveryRider and PickupRider permission sets (AuthService.RegisterOrganizationAsync).
const RIDER_PERMISSIONS = new Set(["delivery.attempt", "delivery.complete", "cod.collect", "shipment.scan"]);

export function hasStaffPermissions(permissions: string[]): boolean {
  return permissions.some((p) => !RIDER_PERMISSIONS.has(p));
}

export function firstName(fullName: string): string {
  return fullName.trim().split(/\s+/)[0] ?? "";
}

export const FAILURE_REASONS: RecordFailedAttemptFields["reason"][] = [
  "CustomerUnavailable", "PhoneUnreachable", "WrongAddress", "IncompleteAddress",
  "CustomerRefused", "CustomerRequestedReschedule", "CashUnavailable", "DamagedParcel", "Other",
];

const REASON_LABELS: Record<RecordFailedAttemptFields["reason"], string> = {
  CustomerUnavailable: "Customer unavailable",
  PhoneUnreachable: "Phone unreachable",
  WrongAddress: "Wrong address",
  IncompleteAddress: "Incomplete address",
  CustomerRefused: "Customer refused",
  CustomerRequestedReschedule: "Customer asked to reschedule",
  CashUnavailable: "Customer has no cash",
  DamagedParcel: "Parcel damaged",
  Other: "Other",
};

export function failureReasonLabel(reason: RecordFailedAttemptFields["reason"]): string {
  return REASON_LABELS[reason];
}

export function taskResultLabel(task: Pick<MyTodayTask, "status" | "type">): string {
  if (task.status === "Failed") return "Failed";
  if (task.status === "Completed") return task.type === "Pickup" ? "Picked up" : "Delivered";
  return "Pending";
}
