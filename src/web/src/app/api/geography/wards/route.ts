import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const localLevelId = request.nextUrl.searchParams.get("localLevelId");
  if (!localLevelId) {
    return NextResponse.json({ error: "localLevelId is required" }, { status: 400 });
  }
  try {
    const response = await apiFetch(`/api/v1/geography/wards?localLevelId=${localLevelId}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
