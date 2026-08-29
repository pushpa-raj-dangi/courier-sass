"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Province, District, LocalLevel } from "@/features/geography/types";

export function useProvinces() {
  return useQuery({
    queryKey: ["geography", "provinces"],
    queryFn: () => clientFetch<Province[]>("/api/geography/provinces"),
  });
}

export function useDistricts(provinceId: string | null) {
  return useQuery({
    queryKey: ["geography", "districts", provinceId],
    queryFn: () => clientFetch<District[]>(`/api/geography/districts?provinceId=${provinceId}`),
    enabled: provinceId !== null,
  });
}

export function useLocalLevels(districtId: string | null) {
  return useQuery({
    queryKey: ["geography", "local-levels", districtId],
    queryFn: () => clientFetch<LocalLevel[]>(`/api/geography/local-levels?districtId=${districtId}`),
    enabled: districtId !== null,
  });
}
