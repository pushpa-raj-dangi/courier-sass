import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET(_request: Request, { params }: { params: { id: string } }) {
  try {
    const response = await merchantApiFetch(`/api/v1/cod/settlements/${params.id}/export`);
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
