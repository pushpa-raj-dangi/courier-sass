import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/geography/districts", () => {
  it("returns 400 when provinceId is missing", async () => {
    const request = new NextRequest("http://localhost/api/geography/districts");

    const response = await GET(request);

    expect(response.status).toBe(400);
    expect(apiFetch).not.toHaveBeenCalled();
  });

  it("forwards provinceId to the backend and proxies the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: "d1", provinceId: "p1", name: "Kathmandu" }],
    } as Response);
    const request = new NextRequest("http://localhost/api/geography/districts?provinceId=p1");

    const response = await GET(request);

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/geography/districts?provinceId=p1");
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual([{ id: "d1", provinceId: "p1", name: "Kathmandu" }]);
  });
});
