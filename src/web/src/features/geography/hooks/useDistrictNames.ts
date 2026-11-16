"use client";

import { useQueries } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { District } from "@/features/geography/types";

/**
 * District id → name for every distinct province given. Shares `useDistricts`' query key, so a
 * province already read by a picker is not fetched again. Unknown ids are simply absent.
 */
export function useDistrictNames(provinceIds: string[]): Map<string, string> {
  const unique = Array.from(new Set(provinceIds)).sort();
  return useQueries({
    queries: unique.map((provinceId) => ({
      queryKey: ["geography", "districts", provinceId],
      queryFn: () => clientFetch<District[]>(`/api/geography/districts?provinceId=${provinceId}`),
      staleTime: Infinity,
    })),
    combine: (results) => {
      const names = new Map<string, string>();
      for (const result of results) {
        for (const district of result.data ?? []) names.set(district.id, district.name);
      }
      return names;
    },
  });
}
