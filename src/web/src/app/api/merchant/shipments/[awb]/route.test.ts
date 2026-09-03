import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/shipments/[awb]", () => {
  it("proxies the detail request for the given awb", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", awb: "AWB123" }),
    } as Response);

    const response = await GET(new Request("http://localhost/api/merchant/shipments/AWB123"), {
      params: { awb: "AWB123" },
    });

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/shipments/AWB123");
    expect(response.status).toBe(200);
  });
});
