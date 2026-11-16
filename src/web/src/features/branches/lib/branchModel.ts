import type { Branch, BranchActivityRow, BranchType } from "@/features/branches/types";

export const BRANCH_TYPES: { type: BranchType; label: string }[] = [
  { type: "HeadOffice", label: "Head office" },
  { type: "Hub", label: "Hub" },
  { type: "Branch", label: "Branch" },
  { type: "PickupPoint", label: "Pickup point" },
  { type: "Warehouse", label: "Warehouse" },
];

export function parseBranchType(value: string | null): BranchType | undefined {
  return BRANCH_TYPES.find((t) => t.type === value)?.type;
}

export function branchTypeLabel(type: BranchType): string {
  return BRANCH_TYPES.find((t) => t.type === type)?.label ?? type;
}

export function matchesBranchQuery(branch: Pick<Branch, "code" | "name">, q: string): boolean {
  const needle = q.trim().toLowerCase();
  if (!needle) return true;
  return branch.code.toLowerCase().includes(needle) || branch.name.toLowerCase().includes(needle);
}

export function filterBranches(branches: Branch[], filters: { type?: BranchType; q: string; showInactive: boolean }): Branch[] {
  return branches.filter(
    (b) =>
      (filters.showInactive || b.isActive) &&
      (!filters.type || b.type === filters.type) &&
      matchesBranchQuery(b, filters.q),
  );
}

/** The statuses GET /branches/activity counts as "On hand" — kept in step with the backend. */
export const ON_HAND_STATUSES = ["AtOriginBranch", "Sorted", "AtDestinationBranch", "RtoInitiated", "RtoAtOrigin"] as const;

// The shipments list's branchId filter matches origin, destination or current branch, so these
// lists can be longer than the custody count — link text says "View shipments", never a number.
export function onHandShipmentsHref(branchId: string): string {
  const params = new URLSearchParams({ branchId });
  ON_HAND_STATUSES.forEach((s) => params.append("status", s));
  return `/shipments?${params.toString()}`;
}

export function outForDeliveryHref(branchId: string): string {
  return `/shipments?${new URLSearchParams({ branchId, status: "OutForDelivery" }).toString()}`;
}

export function ridersOutHref(branchId: string): string {
  return `/riders?${new URLSearchParams({ branch: branchId, stage: "on" }).toString()}`;
}

/** A branch missing from a loaded activity response has no activity. */
export function zeroActivity(branchId: string): BranchActivityRow {
  return { branchId, onHand: 0, outForDelivery: 0, ridersOut: 0, openExceptions: 0 };
}
