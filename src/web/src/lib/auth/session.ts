import { cookies } from "next/headers";
import { authService } from "@/services/authService";

const ACCESS_COOKIE = "courier_access_token";
const REFRESH_COOKIE = "courier_refresh_token";

const cookieOptions = {
  httpOnly: true,
  secure: process.env.NODE_ENV === "production",
  sameSite: "lax" as const,
  path: "/",
};

// accessTokenMaxAgeSeconds defaults to 15 minutes, correct for an ordinary staff login (whose
// access token silently refreshes via getValidAccessToken below). The impersonation route passes
// its token's REAL remaining lifetime (a 30-minute JWT with no refresh capability) so the cookie
// doesn't expire while the impersonation banner still shows time remaining.
export function setSessionCookies(accessToken: string, refreshToken: string, accessTokenMaxAgeSeconds: number = 60 * 15) {
  const store = cookies();
  store.set(ACCESS_COOKIE, accessToken, { ...cookieOptions, maxAge: accessTokenMaxAgeSeconds });
  store.set(REFRESH_COOKIE, refreshToken, { ...cookieOptions, maxAge: 60 * 60 * 24 * 7 });
}

export function clearSessionCookies() {
  const store = cookies();
  store.delete(ACCESS_COOKIE);
  store.delete(REFRESH_COOKIE);
}

// Returns a usable access token, transparently refreshing (and re-setting cookies) if the
// stored access token has expired. Returns null only when there is no session at all, or the
// refresh token itself is invalid/expired/revoked — callers treat null as "not logged in".
export async function getValidAccessToken(): Promise<string | null> {
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
    const tokens = await authService.refresh(refreshToken);
    setSessionCookies(tokens.accessToken, tokens.refreshToken);
    return tokens.accessToken;
  } catch {
    clearSessionCookies();
    return null;
  }
}
