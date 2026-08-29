"use client";

import { useSession } from "@/features/auth/hooks/useSession";

// A branch-scoped check only ever narrows access further — it never grants a permission the
// user's overall permission set doesn't already contain. Branch-level enforcement of *who can
// act on which branch's data* still happens server-side on every request (CLAUDE.md §33); this
// hook only controls what the UI shows, matching hasAllBranchesScope/branches from /auth/me.
export function usePermission(permission: string, options?: { branchId?: string }): boolean {
  const { data: session } = useSession();
  if (!session) {
    return false;
  }
  if (!session.permissions.includes(permission)) {
    return false;
  }
  if (!options?.branchId) {
    return true;
  }
  if (session.hasAllBranchesScope) {
    return true;
  }
  return session.branches.some((b) => b.branchId === options.branchId);
}
