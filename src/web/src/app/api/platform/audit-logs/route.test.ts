import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/platformApiFetch", () => ({
  platformApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";
import { NextRequest } from "next/server";

describe("GET /api/platform/audit-logs", () => {
  it("proxies the query string to the backend", async () => {
    vi.mocked(platformApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }),
    } as Response);

    const request = new NextRequest("http://localhost/api/platform/audit-logs?organizationSearch=kathmandu");
    const response = await GET(request);

    expect(platformApiFetch).toHaveBeenCalledWith("/api/v1/platform/audit-logs?organizationSearch=kathmandu");
    expect(response.status).toBe(200);
  });
});
