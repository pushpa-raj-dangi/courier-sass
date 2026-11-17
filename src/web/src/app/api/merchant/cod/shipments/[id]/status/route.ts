import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/cod/merchant-shipments/${params.id}/cod-status`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
