import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/manifests/[id]", () => {
  it("proxies the backend response for the given id", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "m1" }),
    } as Response);

    const response = await GET({} as Request, { params: { id: "m1" } });

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/manifests/m1");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET({} as Request, { params: { id: "m1" } });

    expect(response.status).toBe(401);
  });
});
