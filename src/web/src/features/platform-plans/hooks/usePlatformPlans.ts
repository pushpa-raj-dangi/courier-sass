"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Plan } from "@/features/platform-plans/types";

export function usePlatformPlans() {
  return useQuery({
    queryKey: ["platform-plans"],
    queryFn: () => clientFetch<Plan[]>("/api/platform/plans"),
  });
}
