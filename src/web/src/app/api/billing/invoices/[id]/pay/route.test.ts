import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";
import { NextRequest } from "next/server";

describe("POST /api/billing/invoices/[id]/pay", () => {
  it("proxies the gateway and returnBaseUrl to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ redirectUrl: "https://rc-epay.esewa.com.np/api/epay/main/v2/form", formFields: { amount: "1500.00" } }),
    } as Response);

    const request = new NextRequest("http://localhost/api/billing/invoices/inv-1/pay", {
      method: "POST",
      body: JSON.stringify({ gateway: "Esewa", returnBaseUrl: "http://localhost:3000/billing/callback" }),
    });
    const response = await POST(request, { params: { id: "inv-1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/billing/invoices/inv-1/pay",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ gateway: "Esewa", returnBaseUrl: "http://localhost:3000/billing/callback" }),
      })
    );
    expect(response.status).toBe(200);
  });

  it("returns 401 when the session is missing", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new NextRequest("http://localhost/api/billing/invoices/inv-1/pay", {
      method: "POST",
      body: JSON.stringify({ gateway: "Khalti", returnBaseUrl: "http://localhost:3000/billing/callback" }),
    });
    const response = await POST(request, { params: { id: "inv-1" } });

    expect(response.status).toBe(401);
  });
});
