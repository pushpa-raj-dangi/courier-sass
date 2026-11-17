import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({ merchantApiFetch: vi.fn() }));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/cod/shipments/[id]/status", () => {
  it("forwards to the shipment cod-status endpoint", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({ status: 200, json: async () => ({ state: "Prepaid" }) } as Response);
    const response = await GET(new Request("http://localhost"), { params: { id: "abc" } });
    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/cod/merchant-shipments/abc/cod-status");
    expect(response.status).toBe(200);
  });

  it("passes a 404 through", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({ status: 404, json: async () => null } as Response);
    const response = await GET(new Request("http://localhost"), { params: { id: "x" } });
    expect(response.status).toBe(404);
  });

  it("returns 401 when merchantApiFetch throws", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost"), { params: { id: "abc" } });
    expect(response.status).toBe(401);
  });
});
