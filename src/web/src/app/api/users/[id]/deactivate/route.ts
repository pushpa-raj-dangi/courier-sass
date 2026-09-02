import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function PATCH(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/users/${params.id}/deactivate`, { method: "PATCH" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
