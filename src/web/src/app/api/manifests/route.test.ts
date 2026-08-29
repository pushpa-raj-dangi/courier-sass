import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/manifests", () => {
  it("passes branchId/status through to the backend and proxies the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const request = new NextRequest("http://localhost/api/manifests?branchId=b1&status=Draft");
    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/manifests?branchId=b1&status=Draft");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/manifests");
    const response = await GET(request);

    expect(response.status).toBe(401);
  });
});

describe("POST /api/manifests", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "m1", code: "MNF-20260829-001" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/manifests", {
      method: "POST",
      body: JSON.stringify({ fromBranchId: "b1", toBranchId: "b2", purpose: "Forward" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/manifests",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(201);
  });
});
