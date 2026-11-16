import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

import { GET } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders/board", () => {
  it("forwards the query string to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({ ok: true, status: 200, json: async () => ({ items: [] }) } as Response);
    const response = await GET(new NextRequest("http://localhost/api/riders/board?branchId=b1"));
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/board?branchId=b1");
    expect(response.status).toBe(200);
  });

  it("returns 401 when apiFetch throws", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new NextRequest("http://localhost/api/riders/board"));
    expect(response.status).toBe(401);
  });
});
