import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/platformApiFetch", () => ({
  platformApiFetch: vi.fn(),
}));

import { GET } from "./route";
import { platformApiFetch } from "@/lib/auth/platformApiFetch";

describe("GET /api/platform/queues", () => {
  it("proxies to the backend queues endpoint", async () => {
    vi.mocked(platformApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(platformApiFetch).toHaveBeenCalledWith("/api/v1/platform/queues");
    expect(response.status).toBe(200);
  });

  it("returns 401 when there is no valid platform session", async () => {
    vi.mocked(platformApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});
