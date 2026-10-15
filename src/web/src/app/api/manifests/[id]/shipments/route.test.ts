import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/manifests/[id]/shipments", () => {
  it("proxies to the backend parcel list", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => [] } as unknown as Response);
    const response = await GET(new Request("http://localhost/api/manifests/m1/shipments"), { params: { id: "m1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/manifests/m1/shipments");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/manifests/m1/shipments"), { params: { id: "m1" } });
    expect(response.status).toBe(401);
  });
});
