import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/geography/provinces", () => {
  it("proxies to the backend via merchantApiFetch and returns the response", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: "p1", name: "Bagmati" }],
    } as Response);

    const response = await GET();

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/geography/provinces");
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual([{ id: "p1", name: "Bagmati" }]);
  });

  it("returns 401 when merchantApiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});
