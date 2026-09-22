"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Country } from "@/features/platform-countries/types";

export function useUpdateCountry() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isServiced, countryZoneId }: { id: string; isServiced: boolean; countryZoneId: string | null }) =>
      clientFetch<Country>(`/api/platform/countries/${id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ isServiced, countryZoneId }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-countries"] }),
  });
}
