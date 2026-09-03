import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/pickups/${params.id}/cancel`, { method: "POST" });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
