import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/me/today", () => {
  it("proxies the rider's day and keeps a 404", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: false, status: 404, json: async () => null } as Response);
    const response = await GET();
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/me/today");
    expect(response.status).toBe(404);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET();
    expect(response.status).toBe(401);
  });
});
