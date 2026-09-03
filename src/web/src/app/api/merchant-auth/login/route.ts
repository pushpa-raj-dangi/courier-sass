import { NextRequest, NextResponse } from "next/server";
import { merchantAuthService } from "@/services/merchantAuthService";
import { setMerchantSessionCookies } from "@/lib/auth/merchantSession";

export async function POST(request: NextRequest) {
  const { email, password } = await request.json();
  try {
    const tokens = await merchantAuthService.login(email, password);
    setMerchantSessionCookies(tokens.accessToken, tokens.refreshToken);
    return NextResponse.json({ ok: true });
  } catch {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }
}
