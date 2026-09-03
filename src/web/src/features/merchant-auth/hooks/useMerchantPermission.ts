"use client";

import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";

// No branch-scope parameter — unlike staff's usePermission, merchants have no branch concept to
// narrow by (RBAC.md §5: merchants hold a small fixed permission set per role, Owner or Staff,
// no custom roles and no per-branch grants).
export function useMerchantPermission(permission: string): boolean {
  const { data: session } = useMerchantSession();
  if (!session) {
    return false;
  }
  return session.permissions.includes(permission);
}
