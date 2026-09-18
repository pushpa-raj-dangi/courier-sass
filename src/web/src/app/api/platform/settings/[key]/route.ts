import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function PUT(request: Request, { params }: { params: { key: string } }) {
  const requestBody = await request.text();
  try {
    const response = await platformApiFetch(`/api/v1/platform/settings/${params.key}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
