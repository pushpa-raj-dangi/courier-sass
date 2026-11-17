import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/merchantApiFetch", () => ({ merchantApiFetch: vi.fn() }));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/cod/awaiting", () => {
  it("forwards the query string to the merchant-awaiting endpoint", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({ status: 200, json: async () => ({ items: [] }) } as Response);
    const response = await GET(new NextRequest("http://localhost/api/merchant/cod/awaiting?page=2&pageSize=20"));
    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/cod/merchant-awaiting?page=2&pageSize=20");
    expect(response.status).toBe(200);
  });

  it("returns 401 when merchantApiFetch throws", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new NextRequest("http://localhost/api/merchant/cod/awaiting"));
    expect(response.status).toBe(401);
  });
});
