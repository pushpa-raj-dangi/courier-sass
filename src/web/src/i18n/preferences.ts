// Non-sensitive UI preferences, so a plain (non-HttpOnly) cookie is correct: client code sets it
// and the server reads it on the next request (src/i18n/request.ts, src/app/layout.tsx).
const ONE_YEAR_SECONDS = 60 * 60 * 24 * 365;

export function setPreferenceCookie(name: string, value: string): void {
  document.cookie = `${name}=${encodeURIComponent(value)}; Path=/; Max-Age=${ONE_YEAR_SECONDS}; SameSite=Lax`;
}

export function readCookie(name: string): string | null {
  const prefix = `${name}=`;
  for (const part of document.cookie.split("; ")) {
    if (part.startsWith(prefix)) {
      return decodeURIComponent(part.slice(prefix.length));
    }
  }
  return null;
}
