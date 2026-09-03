import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/pricing/quote", {
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
