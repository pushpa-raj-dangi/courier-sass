import { NextResponse } from "next/server";
import { clearPlatformSessionCookies } from "@/lib/auth/platformSession";

export async function POST() {
  clearPlatformSessionCookies();
  return NextResponse.json({ ok: true });
}
