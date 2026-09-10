import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";
import { setSessionCookies } from "@/lib/auth/session";

// Deliberately writes into the ordinary STAFF session cookie slot (setSessionCookies), not the
// platform one — the whole point of impersonation is to make the SuperAdmin's browser act as if
// it were a real staff login for the target org's user. See lib/auth/platformSession.ts for the
// separate, non-overlapping platform session cookies this route must never touch.
export async function POST(_request: Request, { params }: { params: { id: string; userId: string } }) {
  try {
    const response = await platformApiFetch(
      `/api/v1/platform/organizations/${params.id}/users/${params.userId}/impersonate`,
      { method: "POST" }
    );
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      return NextResponse.json(body, { status: response.status });
    }
    const body = await response.json();

    // The impersonation token has a real 30-minute JWT lifetime but no refresh capability, so
    // the access-token cookie must be sized to match — not the ordinary 15-minute default (which
    // would silently bounce the operator to /login at T+15min while the countdown banner, driven
    // by the separate courier_impersonation_meta cookie below, still shows ~15 minutes left).
    const accessTokenMaxAgeSeconds = Math.max(1, Math.floor((new Date(body.expiresAt).getTime() - Date.now()) / 1000));

    // setSessionCookies also sets a refresh-token cookie sized for 7 days — overwritten
    // immediately below with an empty, already-expired one, since Task 11's impersonation
    // token has no matching refresh token at all (no renewal, by design).
    setSessionCookies(body.accessToken, "", accessTokenMaxAgeSeconds);
    const result = NextResponse.json({ ok: true });
    result.cookies.set("courier_refresh_token", "", { maxAge: 0, path: "/" });
    result.cookies.set(
      "courier_impersonation_meta",
      JSON.stringify({ name: body.impersonatedUserName, organizationName: body.organizationName, expiresAt: body.expiresAt }),
      { httpOnly: false, sameSite: "lax", path: "/", expires: new Date(body.expiresAt) }
    );
    return result;
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
