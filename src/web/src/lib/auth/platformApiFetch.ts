import { getUpstreamApiUrl } from "@/lib/env";
import { getValidPlatformAccessToken } from "@/lib/auth/platformSession";

// Server-only helper for platform-track BFF routes to call the real API with the current
// SuperAdmin session's bearer token attached, refreshing transparently. Mirrors
// lib/auth/apiFetch.ts and lib/auth/merchantApiFetch.ts exactly, kept as a separate file rather
// than a shared parameterized helper — the three token tracks (staff, merchant, platform) must
// never be interchangeable at this layer.
export async function platformApiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const accessToken = await getValidPlatformAccessToken();
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
