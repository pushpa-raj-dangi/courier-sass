import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/tasks", () => {
  it("proxies the assignment request to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: "t1" }),
    } as Response);

    const request = new Request("http://localhost/api/riders/tasks", {
      method: "POST",
      body: JSON.stringify({ riderId: "r1", shipmentId: "s1", type: "Pickup" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/tasks",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(200);
  });
});
