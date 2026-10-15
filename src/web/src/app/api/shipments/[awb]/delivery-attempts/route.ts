import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

// The segment is named [awb] because Next.js allows one dynamic name per level and the sibling
// /api/shipments/[awb] routes already use it; here it carries the shipment id.
export async function GET(_request: Request, { params }: { params: { awb: string } }) {
  try {
    const response = await apiFetch(`/api/v1/shipments/${encodeURIComponent(params.awb)}/delivery-attempts`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
