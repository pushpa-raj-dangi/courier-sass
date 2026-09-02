import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/reports/dashboard/organization", () => {
  it("proxies the backend response body on success", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ todaysShipmentsByStatus: { Created: 3 } }),
    } as Response);

    const request = new NextRequest("http://localhost/api/reports/dashboard/organization");
    const response = await GET(request);

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ todaysShipmentsByStatus: { Created: 3 } });
  });

  it("forwards the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({}),
    } as Response);

    const request = new NextRequest("http://localhost/api/reports/dashboard/organization?from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z");
    await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/reports/dashboard/organization?from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z");
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/reports/dashboard/organization");
    const response = await GET(request);

    expect(response.status).toBe(401);
  });

  it("propagates the backend's own non-OK status", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => ({ error: "forbidden" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/reports/dashboard/organization");
    const response = await GET(request);

    expect(response.status).toBe(403);
  });
});
