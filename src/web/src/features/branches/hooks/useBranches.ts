"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Branch } from "@/features/branches/types";

export function useBranches() {
  return useQuery({
    queryKey: ["branches"],
    queryFn: () => clientFetch<Branch[]>("/api/branches"),
  });
}
