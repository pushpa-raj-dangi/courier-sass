import { NextRequest, NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/merchant/staff");
    // Auth-failure responses (401/403) from the backend carry an empty body
    // (bare Results.Forbid()/Results.Unauthorized()) — response.json() throws
    // SyntaxError on empty input, which the outer catch used to swallow into a
    // misleading generic 401, masking a real 403 (found via Frontend Phase F10
    // Task 16 E2E pass: a Staff-role session hitting this Owner-only route saw
    // 401 instead of the backend's actual 403). .catch(() => null) preserves
    // the real status code while tolerating a bodyless response.
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await merchantApiFetch("/api/v1/merchant/staff", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
