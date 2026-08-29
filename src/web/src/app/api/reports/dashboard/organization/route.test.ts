import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/reports/dashboard/organization", () => {
  it("proxies the backend response body on success", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ todaysShipmentsByStatus: { Created: 3 } }),
    } as Response);

    const response = await GET();

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ todaysShipmentsByStatus: { Created: 3 } });
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });

  it("propagates the backend's own non-OK status", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => ({ error: "forbidden" }),
    } as Response);

    const response = await GET();

    expect(response.status).toBe(403);
  });
});
