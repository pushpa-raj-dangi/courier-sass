import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET(request: NextRequest) {
  const districtId = request.nextUrl.searchParams.get("districtId");
  if (!districtId) {
    return NextResponse.json({ error: "districtId is required" }, { status: 400 });
  }
  try {
    const response = await merchantApiFetch(`/api/v1/geography/local-levels?districtId=${districtId}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
