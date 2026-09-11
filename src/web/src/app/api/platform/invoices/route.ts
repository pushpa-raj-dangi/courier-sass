import { NextRequest, NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await platformApiFetch(`/api/v1/platform/invoices${query}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
