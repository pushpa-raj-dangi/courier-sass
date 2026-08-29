"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export interface Rider {
  id: string;
  licenseNumber: string;
  isActive: boolean;
}

export function useRiders() {
  return useQuery({
    queryKey: ["riders"],
    queryFn: () => clientFetch<Rider[]>("/api/riders"),
  });
}
