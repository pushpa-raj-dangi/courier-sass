import { NextRequest, NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

// Proxies GET /api/v1/settings/organization/defaults — the narrow, shipment.create-gated route
// (Finding 2 fix) consumed by useShipmentDefaults for the shipment-creation flow. Distinct from
// the org.settings.manage-gated GET /api/v1/settings/organization used by the admin Settings UI.
export async function GET() {
  try {
    const response = await apiFetch("/api/v1/settings/organization/defaults");
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}

export async function PATCH(request: NextRequest) {
  const requestBody = await request.text();
  try {
    const response = await apiFetch("/api/v1/settings/organization/defaults", {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: requestBody,
    });
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
