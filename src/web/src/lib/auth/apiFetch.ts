import { getUpstreamApiUrl } from "@/lib/env";
import { getValidAccessToken } from "@/lib/auth/session";

// Server-only helper for later feature phases (F1+) to call the real API with the current
// session's bearer token attached, refreshing transparently via getValidAccessToken.
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const accessToken = await getValidAccessToken();
  if (!accessToken) {
    throw new Error("NOT_AUTHENTICATED");
  }
  return fetch(`${getUpstreamApiUrl()}${path}`, {
    ...init,
    headers: {
      ...init.headers,
      Authorization: `Bearer ${accessToken}`,
    },
    cache: "no-store",
  });
}
