"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

export function useResetPlatformSetting() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) => clientFetch(`/api/platform/settings/${key}/reset`, { method: "POST" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-settings"] }),
  });
}
