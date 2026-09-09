import { cookies } from "next/headers";
import { platformAuthService } from "@/services/platformAuthService";

const ACCESS_COOKIE = "courier_platform_access_token";
const REFRESH_COOKIE = "courier_platform_refresh_token";

const cookieOptions = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/",
};

export function setPlatformSessionCookies(accessToken: string, refreshToken: string) {
  const store = cookies();
  store.set(ACCESS_COOKIE, accessToken, { ...cookieOptions, maxAge: 60 * 15 });
  store.set(REFRESH_COOKIE, refreshToken, { ...cookieOptions, maxAge: 60 * 60 * 24 * 7 });
}

export function clearPlatformSessionCookies() {
  const store = cookies();
  store.delete(ACCESS_COOKIE);
  store.delete(REFRESH_COOKIE);
}

// Same transparent-refresh contract as getValidAccessToken (lib/auth/session.ts) and
// getValidMerchantAccessToken (lib/auth/merchantSession.ts) — returns null only when there is no
// session at all, or the refresh token itself is invalid/expired/revoked.
export async function getValidPlatformAccessToken(): Promise<string | null> {
  const store = cookies();
  const accessToken = store.get(ACCESS_COOKIE)?.value;
  const refreshToken = store.get(REFRESH_COOKIE)?.value;

  if (accessToken) {
    return accessToken;
  }
  if (!refreshToken) {
    return null;
  }

  try {
    const tokens = await platformAuthService.refresh(refreshToken);
    setPlatformSessionCookies(tokens.accessToken, tokens.refreshToken);
    return tokens.accessToken;
  } catch {
    clearPlatformSessionCookies();
    return null;
  }
}
