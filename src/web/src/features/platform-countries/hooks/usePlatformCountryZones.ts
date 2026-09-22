"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CountryZone } from "@/features/platform-countries/types";

export function usePlatformCountryZones() {
  return useQuery({
    queryKey: ["platform-country-zones"],
    queryFn: () => clientFetch<CountryZone[]>("/api/platform/country-zones"),
  });
}
