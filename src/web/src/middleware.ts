import { NextRequest, NextResponse } from "next/server";

// UX-level redirect only — presence of a cookie is not proof of a valid session. The real
// authorization boundary is the backend API on every request (CLAUDE.md §33); if the access
// token cookie is stale and the refresh token is also invalid, /api/auth/session's 401 (Task 4)
// is what actually enforces the redirect the client-side code follows.
export function middleware(request: NextRequest) {
  const hasSession =
    request.cookies.has("courier_access_token") || request.cookies.has("courier_refresh_token");
  if (!hasSession) {
    const loginUrl = new URL("/login", request.url);
    return NextResponse.redirect(loginUrl);
  }
  return NextResponse.next();
}

export const config = {
  matcher: ["/dashboard/:path*", "/shipments/:path*", "/branches/:path*", "/cod/:path*"],
};
