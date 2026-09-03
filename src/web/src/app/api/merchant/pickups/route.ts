import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/pickups");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    // Forward the client-generated Idempotency-Key — the backend's IdempotencyMiddleware requires
    // it for every merchant-JWT POST to /api/v1/pickups (found via Frontend Phase F10 Task 16's
    // live E2E pass: every pickup request 400'd with IDEMPOTENCY_KEY_REQUIRED without this).
    const idempotencyKey = request.headers.get("Idempotency-Key");
    const response = await merchantApiFetch("/api/v1/pickups", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {}),
      },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
