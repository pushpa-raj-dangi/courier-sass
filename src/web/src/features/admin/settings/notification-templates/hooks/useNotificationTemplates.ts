"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { NotificationTemplate } from "@/features/admin/settings/notification-templates/types";

export function useNotificationTemplates() {
  return useQuery({
    queryKey: ["notification-templates"],
    queryFn: () => clientFetch<NotificationTemplate[]>("/api/settings/notification-templates"),
  });
}
