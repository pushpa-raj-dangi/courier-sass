"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { CountryZone } from "@/features/platform-countries/types";

export function useCreateCountryZone() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { name: string; description?: string }) =>
      clientFetch<CountryZone>("/api/platform/country-zones", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["platform-country-zones"] });
      queryClient.invalidateQueries({ queryKey: ["platform-countries"] });
    },
  });
}
