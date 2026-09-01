import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/me", () => {
  it("proxies to the backend and returns the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "r1" }),
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/me");
    expect(response.status).toBe(200);
  });

  it("returns 404 when the backend returns 404", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 404,
      json: async () => null,
    } as Response);

    const response = await GET();

    expect(response.status).toBe(404);
  });
});
