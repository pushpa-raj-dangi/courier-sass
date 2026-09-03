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

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments/s1/cancel", { method: "POST" });
    expect(response.status).toBe(200);
  });
});
