"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { Vehicle, CreateVehicleInput } from "@/features/riders/types";

export function useCreateVehicle() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateVehicleInput) =>
      clientFetch<Vehicle>("/api/vehicles", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["vehicles"] });
    },
  });
}
