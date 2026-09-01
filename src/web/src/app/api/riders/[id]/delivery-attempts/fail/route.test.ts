import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/delivery-attempts/fail", () => {
  it("proxies the failed-attempt request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "a1", succeeded: false }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/delivery-attempts/fail", {
      method: "POST",
      body: JSON.stringify({ shipmentId: "s1", reason: "CustomerUnavailable", customerContacted: true }),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/delivery-attempts/fail",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });
});
