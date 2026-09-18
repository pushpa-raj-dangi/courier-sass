"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { PlatformSetting } from "@/features/platform-settings/types";

export function usePlatformSettings() {
  return useQuery({
    queryKey: ["platform-settings", "list"],
    queryFn: () => clientFetch<PlatformSetting[]>("/api/platform/settings"),
  });
}
