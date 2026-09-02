import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function DELETE(_request: Request, { params }: { params: { id: string; branchId: string } }) {
  try {
    const response = await apiFetch(`/api/v1/users/${params.id}/branches/${params.branchId}`, { method: "DELETE" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
