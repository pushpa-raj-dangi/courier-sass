import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { POST } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("POST /api/merchant/shipments/[awb]/cancel", () => {
  it("proxies the cancel action for the given id", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", currentStatus: "Cancelled" }),
    } as Response);

    const response = await POST(new Request("http://localhost/api/merchant/shipments/s1/cancel"), {
      params: { awb: "s1" },
    });

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments/s1/cancel", { method: "POST", headers: undefined });
    expect(response.status).toBe(200);
  });

  // Regression test for the Task 16 E2E-discovered bug: the backend's IdempotencyMiddleware
  // 400s every merchant-JWT POST to /api/v1/shipments/{id}/cancel with IDEMPOTENCY_KEY_REQUIRED
  // unless an Idempotency-Key header is present — this route must forward the client's header.
  it("forwards the client's Idempotency-Key header to the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", currentStatus: "Cancelled" }),
    } as Response);

    await POST(
      new Request("http://localhost/api/merchant/shipments/s1/cancel", {
        headers: { "Idempotency-Key": "key-456" },
      }),
      { params: { awb: "s1" } }
    );

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments/s1/cancel", {
      method: "POST",
      headers: { "Idempotency-Key": "key-456" },
    });
  });
});
