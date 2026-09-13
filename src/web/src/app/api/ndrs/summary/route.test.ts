import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/ndrs/summary", () => {
  it("proxies to the backend summary endpoint", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ Open: 4, Contacting: 2, CustomerConfirmed: 1, ReattemptScheduled: 1, RtoRequested: 1, Resolved: 6 }),
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/ndrs/summary");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});
