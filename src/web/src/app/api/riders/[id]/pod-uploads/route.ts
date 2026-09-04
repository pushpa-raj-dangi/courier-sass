import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request, { params }: { params: { id: string } }) {
  const { searchParams } = new URL(request.url);
  const kind = searchParams.get("kind") ?? "";
  const contentType = request.headers.get("content-type") ?? "application/octet-stream";
  const idempotencyKey = request.headers.get("Idempotency-Key");
  const bodyBuffer = await request.arrayBuffer();
  try {
    const response = await apiFetch(`/api/v1/riders/${params.id}/pod-uploads?kind=${kind}`, {
      method: "POST",
      headers: {
        "Content-Type": contentType,
        ...(idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {}),
      },
      body: bodyBuffer,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
