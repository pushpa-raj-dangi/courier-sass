import { NextRequest, NextResponse } from "next/server";
import { platformAuthService } from "@/services/platformAuthService";
import { setPlatformSessionCookies } from "@/lib/auth/platformSession";

export async function POST(request: NextRequest) {
  const { email, password } = await request.json();
  try {
    const tokens = await platformAuthService.login(email, password);
    setPlatformSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }
}
