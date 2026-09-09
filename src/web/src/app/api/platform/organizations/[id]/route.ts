import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await platformApiFetch(`/api/v1/platform/organizations/${params.id}`);
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
