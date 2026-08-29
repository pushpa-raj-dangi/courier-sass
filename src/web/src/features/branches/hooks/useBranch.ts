"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/branches/types";

export function useBranch(id: string) {
  return useQuery({
    queryKey: ["branches", id],
    queryFn: () => clientFetch<Branch>(`/api/branches/${id}`),
  });
}
