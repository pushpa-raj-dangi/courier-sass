import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("POST /api/billing/payments/[id]/callback", () => {
  it("proxies the callback query parameters to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ succeeded: true, message: "Payment confirmed." }),
    } as Response);

    const request = new NextRequest("http://localhost/api/billing/payments/pay-1/callback", {
      method: "POST",
      body: JSON.stringify({ pidx: "abc123" }),
    });
    const response = await POST(request, { params: { id: "pay-1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/billing/payments/pay-1/callback",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ pidx: "abc123" }) })
    );
    expect(response.status).toBe(200);
  });

  it("returns 401 when the session is missing", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/billing/payments/pay-1/callback", {
      method: "POST",
      body: JSON.stringify({}),
    });
    const response = await POST(request, { params: { id: "pay-1" } });

    expect(response.status).toBe(401);
  });
});
