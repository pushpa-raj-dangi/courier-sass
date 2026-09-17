import { describe, expect, it, vi } from "vitest";
import { POST } from "./route";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

describe("POST /api/riders/me/location", () => {
  it("forwards the ping to the backend and returns its status with no body", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockResolvedValue(new Response(null, { status: 204 }));

    const request = new Request("http://localhost/api/riders/me/location", {
      method: "POST",
      body: JSON.stringify({ latitude: 27.7, longitude: 85.3, accuracyMeters: null, recordedAt: "2026-09-17T00:00:00Z" }),
    });
    const response = await POST(request);

    expect(response.status).toBe(204);
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/me/location", expect.objectContaining({ method: "POST" }));
  });

  it("returns 401 when apiFetch throws", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockRejectedValue(new Error("no session"));

    const request = new Request("http://localhost/api/riders/me/location", { method: "POST", body: "{}" });
    const response = await POST(request);

    expect(response.status).toBe(401);
  });
});
