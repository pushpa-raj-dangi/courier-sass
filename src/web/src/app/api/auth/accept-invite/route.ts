import { NextRequest, NextResponse } from "next/server";
import { authService } from "@/services/authService";
import { setSessionCookies } from "@/lib/auth/session";

export async function POST(request: NextRequest) {
  const { token, password } = await request.json();
  try {
    const tokens = await authService.acceptInvite(token, password);
    setSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "This invite link is invalid or has expired." }, { status: 400 });
  }
}
