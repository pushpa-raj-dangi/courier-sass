import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET(request: NextRequest) {
  const query = request.nextUrl.search;
  try {
    const response = await apiFetch(`/api/v1/reports/export/shipments${query}`);
    return new NextResponse(response.body, {
      status: response.status,
      headers: {
        "Content-Type": response.headers.get("Content-Type") ?? "text/csv",
        "Content-Disposition": response.headers.get("Content-Disposition") ?? "attachment",
      },
    });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
