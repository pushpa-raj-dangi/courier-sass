"use client";

import { usePermission } from "@/features/auth/hooks/usePermission";
import { useAdminUsers } from "@/features/admin/hooks/useAdminUsers";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { useRiders } from "@/features/riders/hooks/useRiders";

// Names for ids on events/custody. Only fetches lookups the viewer may read (user.read,
// rider.read) — otherwise generic "Staff"/"Rider" labels. Server-side names are a follow-up.
export function usePeopleNames() {
  const canReadUsers = usePermission("user.read");
  const canReadRiders = usePermission("rider.read");
  const { data: users } = useAdminUsers({ pageSize: 200, enabled: canReadUsers });
  const { data: riders } = useRiders({ enabled: canReadRiders });
  const { data: branches } = useBranches();

  const userById = new Map((users?.items ?? []).map((u) => [u.id, u.fullName]));
  const riderById = new Map((riders ?? []).map((r) => [r.id, r]));

  return {
    branchName: (id: string | null) => (id ? branches?.find((b) => b.id === id)?.name ?? "Unknown branch" : "—"),
    userName: (id: string | null) => (id ? userById.get(id) ?? "Staff" : "—"),
    riderName: (id: string | null) => {
      if (!id) return "—";
      const rider = riderById.get(id);
      return (rider && userById.get(rider.userId)) ?? "Rider";
    },
  };
}
