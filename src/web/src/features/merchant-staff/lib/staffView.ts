import type { MerchantStaffMember } from "../types";

export function roleCounts(staff: MerchantStaffMember[]): { Owner: number; Staff: number } {
  return {
    Owner: staff.filter((s) => s.role === "Owner").length,
    Staff: staff.filter((s) => s.role === "Staff").length,
  };
}

export function initials(fullName: string): string {
  const words = fullName.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  const first = words[0][0];
  const last = words.length > 1 ? words[words.length - 1][0] : "";
  return (first + last).toUpperCase();
}
