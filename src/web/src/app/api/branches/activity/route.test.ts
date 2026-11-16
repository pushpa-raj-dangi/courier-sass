import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/branches/activity", () => {
  it("proxies the branch activity read", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) } as Response);
    const response = await GET();
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/branches/activity");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET();
    expect(response.status).toBe(401);
  });
});
