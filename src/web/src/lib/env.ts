// Deliberately NOT NEXT_PUBLIC_-prefixed: this is read only from server-side code (Route
// Handlers/services, never a client component — see services/authService.ts and
// lib/auth/apiFetch.ts). A NEXT_PUBLIC_ variable gets inlined as a literal at `next build`
// time (falling back to .env.local when no matching process.env value exists yet at build
// time), which permanently bakes in whatever the *build machine's* local-dev value was and
// makes it impossible to point different deployments (e.g. Docker Compose's internal
// "http://api:8080" vs. host-based "http://localhost:5001") at different API URLs without a
// rebuild. A plain env var is read from process.env at request time instead.
export function getUpstreamApiUrl(): string {
  const url = process.env.API_BASE_URL;
  if (!url) {
    throw new Error("API_BASE_URL is not set");
  }
  return url;
}
