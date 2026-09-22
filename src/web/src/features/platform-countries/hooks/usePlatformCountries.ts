"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PagedResult } from "@/components/data-table/DataTable";
import type { Country } from "@/features/platform-countries/types";

export function usePlatformCountries(search: string, page: number, pageSize: number) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) {
    params.set("search", search);
  }

  return useQuery({
    queryKey: ["platform-countries", "list", search, page, pageSize],
    queryFn: () => clientFetch<PagedResult<Country>>(`/api/platform/countries?${params.toString()}`),
  });
}
