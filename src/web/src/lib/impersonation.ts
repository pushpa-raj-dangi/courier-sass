export interface ImpersonationMeta {
  name: string;
  organizationName: string;
  expiresAt: string;
}

// Pure document.cookie parsing — this file has no server dependency because
// ImpersonationBanner is a client component rendered on every staff page and must read
// this synchronously on each render/interval tick, not through a server round trip.
// Carries only display metadata (name, org name, expiry timestamp) — never a token or secret,
// which is why the cookie backing this is deliberately non-httpOnly (see the BFF route that
// sets it). Must fail safe (return null, never throw) during SSR or on malformed content.
export function readImpersonationMeta(): ImpersonationMeta | null {
  if (typeof document === "undefined") {
    return null;
  }
  const match = document.cookie.match(/(?:^|; )courier_impersonation_meta=([^;]*)/);
  if (!match) {
    return null;
  }
  try {
    return JSON.parse(decodeURIComponent(match[1]));
  } catch {
    return null;
  }
}
