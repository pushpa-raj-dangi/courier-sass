"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MyToday } from "@/features/riders/types";

// Deliberately under the ["riders","me","tasks","today"] prefix: every outbox enqueue and every
// sync drain already invalidates that prefix, so "my day" refreshes with no change to sync code.
export const MY_TODAY_KEY = ["riders", "me", "tasks", "today", "summary"] as const;

export function useMyToday(enabled: boolean) {
  return useQuery({
    queryKey: MY_TODAY_KEY,
    queryFn: () => clientFetch<MyToday>("/api/riders/me/today"),
    enabled,
  });
}
