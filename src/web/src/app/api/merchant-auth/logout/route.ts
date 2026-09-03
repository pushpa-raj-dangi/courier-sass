import { NextResponse } from "next/server";
import { clearMerchantSessionCookies } from "@/lib/auth/merchantSession";

export async function POST() {
  clearMerchantSessionCookies();
  return NextResponse.json({ ok: true });
}
