import { NextResponse } from "next/server";
import { apiFetch } from "@/lib/auth/apiFetch";

export async function GET() {
  try {
    const response = await apiFetch("/api/v1/riders/me");
    // The backend returns a bodyless 404 (no rider profile for this user — a normal outcome
    // for any non-rider account, not an auth failure). response.json() throws on that empty
    // body, and falling into the catch below would misreport this as a 401, which clientFetch
    // treats as a global session-expiry and pops the app-wide reauth dialog for every
    // non-rider user who opens /my-deliveries.
    if (response.status === 404) {
      return NextResponse.json(null, { status: 404 });
    }
    const body = await response.json().catch(() => null);
    return NextResponse.json(body, { status: response.status });
  } catch {
    return NextResponse.json(null, { status: 401 });
  }
}
