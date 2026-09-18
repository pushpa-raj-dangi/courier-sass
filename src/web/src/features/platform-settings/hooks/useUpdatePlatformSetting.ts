"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";

interface UpdatePlatformSettingInput {
  key: string;
  value: string;
  confirmationPhrase?: string;
}

export function useUpdatePlatformSetting() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, value, confirmationPhrase }: UpdatePlatformSettingInput) =>
      clientFetch(`/api/platform/settings/${key}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ value, confirmationPhrase }),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["platform-settings"] }),
  });
}
