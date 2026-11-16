import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/[id]", () => {
  it("proxies the rider single read and keeps a 404", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: false, status: 404, json: async () => null } as Response);
    const response = await GET(new Request("http://localhost/api/riders/r1"), { params: { id: "r1" } });
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/r1");
    expect(response.status).toBe(404);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost/api/riders/r1"), { params: { id: "r1" } });
    expect(response.status).toBe(401);
  });
});
