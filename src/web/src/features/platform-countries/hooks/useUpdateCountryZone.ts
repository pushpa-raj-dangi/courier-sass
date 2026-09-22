"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CountryZone } from "@/features/platform-countries/types";

export function useUpdateCountryZone() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: { id: string; name: string; description?: string | null }) =>
      clientFetch<CountryZone>(`/api/platform/country-zones/${id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["platform-country-zones"] });
      queryClient.invalidateQueries({ queryKey: ["platform-countries"] });
    },
  });
}
