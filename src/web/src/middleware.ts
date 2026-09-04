import { NextRequest, NextResponse } from "next/server";

// UX-level redirect only — presence of a cookie is not proof of a valid session. The real
// authorization boundary is the backend API on every request (CLAUDE.md §33); a stale access
// token with an invalid refresh token is caught by /api/(auth|merchant-auth)/session's 401, which
// the client-side session hooks follow. Two independent branches (staff vs. merchant), since the
// two tracks use different cookies and different login pages.
export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (pathname.startsWith("/merchant/")) {
    const hasMerchantSession =
      request.cookies.has("courier_merchant_access_token") ||
      request.cookies.has("courier_merchant_refresh_token");
    if (!hasMerchantSession) {
      return NextResponse.redirect(new URL("/merchant-login", request.url));
    }
    return NextResponse.next();
  }

  const hasSession =
    request.cookies.has("courier_access_token") || request.cookies.has("courier_refresh_token");
  if (!hasSession) {
    const loginUrl = new URL("/login", request.url);
    return NextResponse.redirect(loginUrl);
  }
  return NextResponse.next();
}

export const config = {
  matcher: [
    "/dashboard/:path*",
    "/shipments/:path*",
    "/branches/:path*",
    "/cod/:path*",
    "/merchant/:path*",
    "/rider/:path*",
  ],
};
