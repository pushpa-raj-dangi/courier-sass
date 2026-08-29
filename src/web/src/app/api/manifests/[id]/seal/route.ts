import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await apiFetch(`/api/v1/manifests/${params.id}/seal`, { method: "POST" });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
