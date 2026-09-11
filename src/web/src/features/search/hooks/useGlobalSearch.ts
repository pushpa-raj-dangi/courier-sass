"use client";

import { useQuery } from "@tanstack/react-query";
import { useDebouncedValue } from "@/lib/hooks/useDebouncedValue";
import { searchEntities } from "@/features/search/api/searchApi";

export function useGlobalSearch(query: string) {
  const debounced = useDebouncedValue(query.trim(), 300);

  return useQuery({
    queryKey: ["global-search", debounced],
    queryFn: () => searchEntities(debounced),
    enabled: debounced.length > 1,
  });
}
