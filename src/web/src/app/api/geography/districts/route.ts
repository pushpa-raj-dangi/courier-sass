import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const provinceId = request.nextUrl.searchParams.get("provinceId");
  if (!provinceId) {
    return NextResponse.json({ error: "provinceId is required" }, { status: 400 });
  }
  try {
    const response = await apiFetch(`/api/v1/geography/districts?provinceId=${provinceId}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
