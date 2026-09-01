import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("GET /api/return-shipments", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const request = new NextRequest("http://localhost/api/return-shipments?originalShipmentId=s1");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/return-shipments?originalShipmentId=s1");
    expect(response.status).toBe(200);
  });
});
