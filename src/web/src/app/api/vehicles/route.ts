import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/vehicles");
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function POST(request: Request) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/vehicles", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json();
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
