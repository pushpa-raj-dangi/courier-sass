import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/[awb]/events", () => {
  it("proxies to the backend events endpoint with an encoded AWB", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => [] } as unknown as Response);

    const response = await GET({} as Request, { params: { awb: "AB 1/2" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/AB%201%2F2/events");
    expect(response.status).toBe(200);
  });

  it("passes a 404 through", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: false, status: 404, json: async () => null } as unknown as Response);
    const response = await GET({} as Request, { params: { awb: "X" } });
    expect(response.status).toBe(404);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET({} as Request, { params: { awb: "X" } });
    expect(response.status).toBe(401);
  });
});
