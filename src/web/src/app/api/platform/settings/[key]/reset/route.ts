import { NextResponse } from "next/server";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

export async function POST(_request: Request, { params }: { params: { key: string } }) {
  try {
    const response = await platformApiFetch(`/api/v1/platform/settings/${params.key}/reset`, { method: "POST" });
    return new NextResponse(null, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
