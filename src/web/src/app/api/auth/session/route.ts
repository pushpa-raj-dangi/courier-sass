import { NextResponse } from "next/server";
import { getValidAccessToken } from "@/lib/auth/session";
import { authService } from "@/services/authService";

export async function GET() {
  const accessToken = await getValidAccessToken();
  if (!accessToken) {
    return NextResponse.json(null, { status: 401 });
  }
  try {
    const me = await authService.me(accessToken);
    return NextResponse.json(me);
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
