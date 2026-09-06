import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

// The dynamic segment is named `awb` only to satisfy Next.js's constraint that sibling
// routes under the same path share one slug name (this directory's sibling `[awb]/route.ts`
// already claims it) — the value passed here is actually the shipment's internal id, since
// the backend's receive-origin transition is keyed by id, not AWB.
export async function POST(_request: Request, { params }: { params: { awb: string } }) {
  try {
    const response = await apiFetch(`/api/v1/shipments/${params.awb}/receive-origin`, {
      method: "POST",
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
