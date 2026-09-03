import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/shipments", () => {
  it("passes page/pageSize through to the backend and proxies the response", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/merchant/shipments?page=2&pageSize=10");
    const response = await GET(request);

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments?page=2&pageSize=10");
    expect(response.status).toBe(200);
  });

  it("returns 401 when merchantApiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/merchant/shipments");
    const response = await GET(request);

    expect(response.status).toBe(401);
  });
});

describe("POST /api/merchant/shipments", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "s1", awb: "AWB123" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/merchant/shipments", {
      method: "POST",
      body: JSON.stringify({ weightKg: 2 }),
    });
    const response = await POST(request);

    expect(merchantApiFetch).toHaveBeenCalledWith(
      "/api/v1/shipments",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ weightKg: 2 }) })
    );
    expect(response.status).toBe(201);
  });
});
