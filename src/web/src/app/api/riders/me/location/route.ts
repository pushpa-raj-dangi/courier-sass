import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function POST(request: Request) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/riders/me/location", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
