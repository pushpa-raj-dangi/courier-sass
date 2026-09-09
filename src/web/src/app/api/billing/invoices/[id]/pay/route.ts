import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: NextRequest, { params }: { params: { id: string } }) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch(`/api/v1/billing/invoices/${params.id}/pay`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
