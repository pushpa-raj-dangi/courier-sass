import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/[awb]", () => {
  it("proxies the backend response for the given awb", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ awb: "AWB123" }),
    } as Response);

    const response = await GET({} as Request, { params: { awb: "AWB123" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/AWB123");
    expect(response.status).toBe(200);
  });

  it("encodes the awb path segment", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({}) } as Response);

    await GET({} as Request, { params: { awb: "A/../B?x" } });

    expect(apiFetch).toHaveBeenLastCalledWith("/api/v1/shipments/A%2F..%2FB%3Fx");
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET({} as Request, { params: { awb: "AWB123" } });

    expect(response.status).toBe(401);
  });
});
