import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/[awb]/delivery-attempts", () => {
  it("proxies the shipment id to the backend attempts read", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) } as unknown as Response);
    const response = await GET(new Request("http://localhost/api/shipments/s1/delivery-attempts"), { params: { awb: "s1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/s1/delivery-attempts");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/shipments/s1/delivery-attempts"), { params: { awb: "s1" } });
    expect(response.status).toBe(401);
  });
});
