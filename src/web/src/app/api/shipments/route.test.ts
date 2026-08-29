import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments", () => {
  it("passes page/pageSize through to the backend and proxies the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/shipments?page=2&pageSize=10");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments?page=2&pageSize=10");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/shipments");
    const response = await GET(request);

    expect(response.status).toBe(401);
  });
});

describe("POST /api/shipments", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "s1", awb: "AWB123" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/shipments", {
      method: "POST",
      body: JSON.stringify({ weightKg: 2 }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/shipments",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ weightKg: 2 }) })
    );
    expect(response.status).toBe(201);
  });
});
