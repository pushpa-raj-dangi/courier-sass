import type { NdrAssignedFilter, NdrReason, NdrStatus } from "@/features/ndr/types";

export const NDR_STAGES: { status: NdrStatus; label: string; token: string }[] = [
  { status: "Open", label: "Open", token: "status-ndr" },
  { status: "Contacting", label: "Contacting", token: "status-out-for-delivery" },
  { status: "CustomerConfirmed", label: "Customer confirmed", token: "status-received" },
  { status: "ReattemptScheduled", label: "Reattempt scheduled", token: "status-sealed" },
  { status: "RtoRequested", label: "RTO requested", token: "status-rto" },
  { status: "Resolved", label: "Resolved", token: "status-delivered" },
];

const REASON_LABELS: Record<NdrReason, string> = {
  CustomerUnavailable: "Customer unavailable",
  PhoneUnreachable: "Phone unreachable",
  WrongAddress: "Wrong address",
  IncompleteAddress: "Incomplete address",
  CustomerRefused: "Customer refused",
  CustomerRequestedReschedule: "Customer asked to reschedule",
  CashUnavailable: "Cash not ready",
  DamagedParcel: "Damaged parcel",
  Other: "Other",
};

export const NDR_REASONS = Object.keys(REASON_LABELS) as NdrReason[];

export function ndrReasonLabel(reason: NdrReason): string {
  return REASON_LABELS[reason] ?? reason;
}

export function ndrStatusLabel(status: NdrStatus): string {
  return NDR_STAGES.find((s) => s.status === status)?.label ?? status;
}

export function parseNdrStatus(value: string | null): NdrStatus | undefined {
  return NDR_STAGES.find((s) => s.status === value)?.status;
}

export function parseNdrReason(value: string | null): NdrReason | undefined {
  return NDR_REASONS.find((r) => r === value);
}

export function parseAssigned(value: string | null): NdrAssignedFilter | undefined {
  return value === "me" || value === "unassigned" ? value : undefined;
}

export function isFollowUpOverdue(ndr: { followUpAt: string | null; status: NdrStatus }, now: Date = new Date()): boolean {
  return ndr.status !== "Resolved" && ndr.followUpAt !== null && Date.parse(ndr.followUpAt) < now.getTime();
}

export function ownerLabel(ownerId: string | null, myUserId: string | undefined, userName: (id: string | null) => string): string {
  if (!ownerId) return "Unassigned";
  return ownerId === myUserId ? "You" : userName(ownerId);
}

// Mirrors NdrService.IsLegalForwardMove: one forward step at a time, CustomerConfirmed branches.
export const NEXT_NDR_STATUSES: Record<NdrStatus, NdrStatus[]> = {
  Open: ["Contacting"],
  Contacting: ["CustomerConfirmed"],
  CustomerConfirmed: ["ReattemptScheduled", "RtoRequested"],
  ReattemptScheduled: [],
  RtoRequested: [],
  Resolved: [],
};
