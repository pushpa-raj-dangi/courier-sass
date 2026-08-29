"use client";

import { useQuery } from "@tanstack/react-query";
import type { MeResponse } from "@/services/authService";

async function fetchSession(): Promise<MeResponse | null> {
  const response = await fetch("/api/auth/session");
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error("SESSION_FETCH_FAILED");
  }
  return response.json();
}

export function useSession() {
  return useQuery({
    queryKey: ["session"],
    queryFn: fetchSession,
    staleTime: 60_000,
    retry: false,
  });
}
