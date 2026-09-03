"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Province, District, LocalLevel } from "@/features/geography/types";

export function useProvinces(enabled = true) {
  return useQuery({
    queryKey: ["geography", "provinces"],
    queryFn: () => clientFetch<Province[]>("/api/geography/provinces"),
    enabled,
  });
}

export function useDistricts(provinceId: string | null, enabled = true) {
  return useQuery({
    queryKey: ["geography", "districts", provinceId],
    queryFn: () => clientFetch<District[]>(`/api/geography/districts?provinceId=${provinceId}`),
    enabled: enabled && provinceId !== null,
  });
}

export function useLocalLevels(districtId: string | null, enabled = true) {
  return useQuery({
    queryKey: ["geography", "local-levels", districtId],
    queryFn: () => clientFetch<LocalLevel[]>(`/api/geography/local-levels?districtId=${districtId}`),
    enabled: enabled && districtId !== null,
  });
}

// Merchant-portal counterparts of the three hooks above: the merchant session has no staff
// cookie, so the shared /api/geography/* BFF routes (apiFetch, staff-only) always 401 for a
// merchant caller. These hit the parallel /api/merchant/geography/* BFF routes (merchantApiFetch)
// instead — same query shape, same backend data (geography reference data carries no
// OrganizationId/MerchantId, so both tracks read the identical rows). Each takes the same
// `enabled` gate as its staff counterpart, so GeographyPicker can mount both hook pairs
// unconditionally (hooks can't be called conditionally) while only the active `source`'s pair
// ever actually fires a request.
export function useMerchantProvinces(enabled = true) {
  return useQuery({
    queryKey: ["merchant", "geography", "provinces"],
    queryFn: () => clientFetch<Province[]>("/api/merchant/geography/provinces"),
    enabled,
  });
}

export function useMerchantDistricts(provinceId: string | null, enabled = true) {
  return useQuery({
    queryKey: ["merchant", "geography", "districts", provinceId],
    queryFn: () => clientFetch<District[]>(`/api/merchant/geography/districts?provinceId=${provinceId}`),
    enabled: enabled && provinceId !== null,
  });
}

export function useMerchantLocalLevels(districtId: string | null, enabled = true) {
  return useQuery({
    queryKey: ["merchant", "geography", "local-levels", districtId],
    queryFn: () => clientFetch<LocalLevel[]>(`/api/merchant/geography/local-levels?districtId=${districtId}`),
    enabled: enabled && districtId !== null,
  });
}
