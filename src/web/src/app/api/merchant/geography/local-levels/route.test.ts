import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/geography/local-levels", () => {
  it("returns 400 when districtId is missing", async () => {
    const request = new NextRequest("http://localhost/api/merchant/geography/local-levels");

    const response = await GET(request);

    expect(response.status).toBe(400);
    expect(merchantApiFetch).not.toHaveBeenCalled();
  });

  it("forwards districtId to the backend via merchantApiFetch and proxies the response", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: "l1", districtId: "d1", name: "Kathmandu Metropolitan City", type: "Metropolitan" }],
    } as Response);
    const request = new NextRequest("http://localhost/api/merchant/geography/local-levels?districtId=d1");

    const response = await GET(request);

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/geography/local-levels?districtId=d1");
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual([
      { id: "l1", districtId: "d1", name: "Kathmandu Metropolitan City", type: "Metropolitan" },
    ]);
  });

  it("returns 401 when merchantApiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const request = new NextRequest("http://localhost/api/merchant/geography/local-levels?districtId=d1");

    const response = await GET(request);

    expect(response.status).toBe(401);
  });
});
