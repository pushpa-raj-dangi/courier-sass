import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await merchantApiFetch(`/api/v1/shipments${query}`);
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/shipments", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
