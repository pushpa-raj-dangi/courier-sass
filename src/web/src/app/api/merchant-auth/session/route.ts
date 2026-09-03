import { NextResponse } from "next/server";
import { getValidMerchantAccessToken } from "@/lib/auth/merchantSession";
import { merchantAuthService } from "@/services/merchantAuthService";

export async function GET() {
  const accessToken = await getValidMerchantAccessToken();
  if (!accessToken) {
    return NextResponse.json(null, { status: 401 });
  }
  try {
    const me = await merchantAuthService.me(accessToken);
    return NextResponse.json(me);
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
