import { NextResponse } from "next/server";
import { getUpstreamApiUrl } from "@/lib/env";

// Genuinely anonymous — unlike every other BFF route in this codebase, this does NOT go through
// apiFetch (lib/auth/apiFetch.ts), which requires a valid session and throws otherwise. Forwards
// the inbound X-Forwarded-For verbatim (never appended to) when present, so the backend's
// IpRateLimitFilter can key on the real visitor's IP once ForwardedHeadersMiddleware trusts this
// server as a known proxy (see
// docs/superpowers/specs/2026-09-04-frontend-phase-f11-public-tracking-design.md §4.2) — absent
// in local dev with no upstream proxy, which the backend already handles safely.
export async function GET(request: Request, { params }: { params: { awb: string } }) {
  const forwardedFor = request.headers.get("x-forwarded-for");
  const upstreamHeaders: Record<string, string> = {};
  if (forwardedFor) {
    upstreamHeaders["X-Forwarded-For"] = forwardedFor;
  }

  const response = await fetch(`${getUpstreamApiUrl()}/track/${params.awb}`, {
    headers: upstreamHeaders,
    cache: "no-store",
  });
  const body = await response.json().catch(() => null);

  const retryAfter = response.headers.get("Retry-After");
  return NextResponse.json(body, {
    status: response.status,
    headers: retryAfter ? { "Retry-After": retryAfter } : undefined,
  });
}
