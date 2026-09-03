import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function POST(request: Request, { params }: { params: { awb: string } }) {
  try {
    // Forward the client-generated Idempotency-Key — the backend's IdempotencyMiddleware requires
    // it for every merchant-JWT POST to /api/v1/shipments/{id}/cancel (found via Frontend Phase
    // F10 Task 16's live E2E pass: every cancel attempt 400'd with IDEMPOTENCY_KEY_REQUIRED
    // without this).
    const idempotencyKey = request.headers.get("Idempotency-Key");
    const response = await merchantApiFetch(`/api/v1/shipments/${params.awb}/cancel`, {
      method: "POST",
      headers: idempotencyKey ? { "Idempotency-Key": idempotencyKey } : undefined,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
