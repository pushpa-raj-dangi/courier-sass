import { NextRequest, NextResponse } from "next/server";
import { authService } from "@/services/authService";
import { setSessionCookies } from "@/lib/auth/session";

export async function POST(request: NextRequest) {
  const { organizationName, organizationSlug, ownerFullName, ownerEmail, ownerPassword } = await request.json();
  try {
    const tokens = await authService.register({
      organizationName,
      organizationSlug,
      ownerFullName,
      ownerEmail,
      ownerPassword,
    });
    setSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch (err) {
    const message = err instanceof Error ? err.message : "Registration failed.";
    return NextResponse.json({ error: message }, { status: 400 });
  }
}
