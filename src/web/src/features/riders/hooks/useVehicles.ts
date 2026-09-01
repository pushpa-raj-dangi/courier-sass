"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Vehicle } from "@/features/riders/types";

export function useVehicles() {
  return useQuery({
    queryKey: ["vehicles"],
    queryFn: () => clientFetch<Vehicle[]>("/api/vehicles"),
  });
}
