import { getUpstreamApiUrl } from "@/lib/env";
import { getValidMerchantAccessToken } from "@/lib/auth/merchantSession";

// Server-only helper for merchant-track BFF routes to call the real API with the current
// merchant session's bearer token attached, refreshing transparently. Mirrors lib/auth/apiFetch.ts
// exactly, kept as a separate file rather than a shared parameterized helper — the two token
// tracks (staff vs. merchant) must never be interchangeable at this layer.
export async function merchantApiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const accessToken = await getValidMerchantAccessToken();
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
