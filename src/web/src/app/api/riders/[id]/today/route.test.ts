import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/[id]/today", () => {
  it("proxies the rider today read", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ rider: {}, tasks: [] }) } as Response);
    const response = await GET(new Request("http://localhost/api/riders/r1/today"), { params: { id: "r1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/r1/today");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/riders/r1/today"), { params: { id: "r1" } });
    expect(response.status).toBe(401);
  });
});
