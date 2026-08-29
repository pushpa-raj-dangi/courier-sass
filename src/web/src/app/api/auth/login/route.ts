import { NextRequest, NextResponse } from "next/server";
import { authService } from "@/services/authService";
import { setSessionCookies } from "@/lib/auth/session";

export async function POST(request: NextRequest) {
  const { email, password } = await request.json();
  try {
    const tokens = await authService.login(email, password);
    setSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }
}
