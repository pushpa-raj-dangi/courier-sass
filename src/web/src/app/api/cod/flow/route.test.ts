import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/cod/flow", () => {
  it("forwards the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ stages: [] }) } as Response);
    const response = await GET(new NextRequest("http://localhost/api/cod/flow?branchId=b1&from=2026-10-01T00:00:00Z"));
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/cod/flow?branchId=b1&from=2026-10-01T00:00:00Z");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new NextRequest("http://localhost/api/cod/flow"));
    expect(response.status).toBe(401);
  });
});
