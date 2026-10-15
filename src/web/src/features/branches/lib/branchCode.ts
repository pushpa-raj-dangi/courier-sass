import type { Branch } from "@/features/branches/types";

/** Short route label: the branch code, else its name, else "—". */
export function branchCode(branches: Branch[] | undefined, id: string | null): string {
  if (!id) return "—";
  const branch = branches?.find((b) => b.id === id);
  if (!branch) return "—";
  return branch.code?.trim() ? branch.code : branch.name;
}
