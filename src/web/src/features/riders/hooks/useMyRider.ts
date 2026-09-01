"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Rider } from "@/features/riders/types";

export function useMyRider() {
  return useQuery({
    queryKey: ["riders", "me"],
    queryFn: () => clientFetch<Rider>("/api/riders/me"),
    retry: false,
  });
}
