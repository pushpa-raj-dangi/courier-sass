import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/merchant/branches");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
