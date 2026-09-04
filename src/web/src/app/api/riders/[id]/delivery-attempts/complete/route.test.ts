import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/delivery-attempts/complete", () => {
  it("proxies the completion request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "a1", succeeded: true }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/delivery-attempts/complete", {
      method: "POST",
      body: JSON.stringify({ shipmentId: "s1", recipientName: "Recipient One", otpVerified: true }),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });

  it("forwards the client's Idempotency-Key header to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "a1", succeeded: true }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/delivery-attempts/complete", {
      method: "POST",
      headers: { "Idempotency-Key": "key-789" },
      body: JSON.stringify({ shipmentId: "s1", recipientName: "Recipient One", otpVerified: true }),
    });
    await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/delivery-attempts/complete",
      expect.objectContaining({ headers: expect.objectContaining({ "Idempotency-Key": "key-789" }) })
    );
  });
});
