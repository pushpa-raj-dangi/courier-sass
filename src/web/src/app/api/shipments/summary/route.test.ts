import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/shipments/summary", () => {
  it("proxies to the backend summary endpoint", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ Created: 2, Delivered: 5 }),
    } as Response);

    const response = await GET(new NextRequest("http://localhost/api/shipments/summary"));

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/shipments/summary");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET(new NextRequest("http://localhost/api/shipments/summary"));

    expect(response.status).toBe(401);
  });
});
