import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/reports/dashboard/branch/[id]", () => {
  it("proxies the backend response for the given branch id", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ missingShipmentExceptions: 0 }),
    } as Response);

    const response = await GET({} as Request, { params: { id: "b1" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/reports/dashboard/branch/b1");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET({} as Request, { params: { id: "b1" } });

    expect(response.status).toBe(401);
  });
});
