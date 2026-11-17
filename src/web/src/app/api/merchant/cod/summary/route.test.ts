import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({ merchantApiFetch: vi.fn() }));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/cod/summary", () => {
  it("forwards to the merchant-summary endpoint", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({ status: 200, json: async () => ({ ok: 1 }) } as Response);
    const response = await GET();
    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/cod/merchant-summary");
    expect(response.status).toBe(200);
  });

  it("returns 401 when merchantApiFetch throws", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    expect((await GET()).status).toBe(401);
  });
});
