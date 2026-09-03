import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/shipments/${params.id}/cancel`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
