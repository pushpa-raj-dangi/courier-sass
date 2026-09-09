import { NextResponse } from "next/server";
import { clearSessionCookies } from "@/lib/auth/session";

// Ends an impersonation session: clears BOTH the staff session cookies (the impersonation
// access token) and the display-only impersonation metadata cookie. A partial clear here could
// leave the banner showing after the underlying session token was invalidated, or vice versa.
export async function POST() {
  clearSessionCookies();
  const result = NextResponse.json({ ok: true });
  result.cookies.set("courier_impersonation_meta", "", { maxAge: 0, path: "/" });
  return result;
}
