import { NextResponse } from "next/server";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

export async function GET() {
  try {
    const response = await merchantApiFetch("/api/v1/merchant/kyc-documents");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

// Raw request body upload, not multipart/form-data — mirrors the backend's own contract
// (MerchantKycEndpoints.cs's doc comment) and app/api/riders/[id]/pod-uploads/route.ts's
// established proxy pattern exactly: forward the original Content-Type header and raw bytes.
export async function POST(request: Request) {
  const { searchParams } = new URL(request.url);
  const documentType = searchParams.get("documentType") ?? "";
  const contentType = request.headers.get("content-type") ?? "application/octet-stream";
  const bodyBuffer = await request.arrayBuffer();
  try {
    const response = await merchantApiFetch(`/api/v1/merchant/kyc-documents?documentType=${documentType}`, {
      method: "POST",
      headers: { "Content-Type": contentType },
      body: bodyBuffer,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
