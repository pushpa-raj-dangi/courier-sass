"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { NotificationTemplate, UpsertNotificationTemplateInput } from "@/features/admin/settings/notification-templates/types";

export function useUpsertNotificationTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpsertNotificationTemplateInput) =>
      clientFetch<NotificationTemplate>("/api/settings/notification-templates", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["notification-templates"] }),
  });
}
