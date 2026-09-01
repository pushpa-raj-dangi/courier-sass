import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/scan/pickup", () => {
  it("proxies the scan request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "s1", currentStatus: "PickedUp" }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/scan/pickup", {
      method: "POST",
      body: JSON.stringify({ awb: "AWB001" }),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/scan/pickup",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });
});
