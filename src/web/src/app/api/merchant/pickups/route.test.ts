import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/pickups", () => {
  it("proxies the response from the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/pickups");
    expect(response.status).toBe(200);
  });
});

describe("POST /api/merchant/pickups", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "p1" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/merchant/pickups", {
      method: "POST",
      body: JSON.stringify({ shipmentId: "s1" }),
    });
    const response = await POST(request);

    expect(merchantApiFetch).toHaveBeenCalledWith(
      "/api/v1/pickups",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ shipmentId: "s1" }) })
    );
    expect(response.status).toBe(201);
  });

  // Regression test for the Task 16 E2E-discovered bug: the backend's IdempotencyMiddleware
  // 400s every merchant-JWT POST to /api/v1/pickups with IDEMPOTENCY_KEY_REQUIRED unless an
  // Idempotency-Key header is present — this route must forward the client's header through.
  it("forwards the client's Idempotency-Key header to the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "p1" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/merchant/pickups", {
      method: "POST",
      headers: { "Idempotency-Key": "key-789" },
      body: JSON.stringify({ shipmentId: "s1" }),
    });
    await POST(request);

    expect(merchantApiFetch).toHaveBeenCalledWith(
      "/api/v1/pickups",
      expect.objectContaining({ headers: expect.objectContaining({ "Idempotency-Key": "key-789" }) })
    );
  });
});
