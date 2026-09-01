"use client";

import { useQuery } from "@tanstack/react-query";
import { clientFetch } from "@/lib/api/clientFetch";
import type { MyTasksToday } from "@/features/riders/types";

export function useMyTasksToday(enabled: boolean) {
  return useQuery({
    queryKey: ["riders", "me", "tasks", "today"],
    queryFn: () => clientFetch<MyTasksToday>("/api/riders/me/tasks/today"),
    enabled,
  });
}
