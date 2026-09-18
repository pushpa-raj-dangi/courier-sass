import { describe, expect, it, vi } from "vitest";
import { POST } from "./route";

vi.mock("@/lib/auth/platformApiFetch", () => ({ platformApiFetch: vi.fn() }));

describe("POST /api/platform/settings/[key]/reset", () => {
  it("forwards the backend's 204 success without throwing while building the response", async () => {
    const { platformApiFetch } = await import("@/lib/auth/platformApiFetch");
    // PlatformSettingsEndpoints MapPost(".../reset") always returns Results.NoContent().
    vi.mocked(platformApiFetch).mockResolvedValue(new Response(null, { status: 204 }));

    const request = new Request("http://localhost/api/platform/settings/Frontend.BaseUrl/reset", { method: "POST" });
    const response = await POST(request, { params: { key: "Frontend.BaseUrl" } });

    expect(response.status).toBe(204);
    expect(platformApiFetch).toHaveBeenCalledWith("/api/v1/platform/settings/Frontend.BaseUrl/reset", { method: "POST" });
  });

  it("returns 401 when platformApiFetch throws (no session)", async () => {
    const { platformApiFetch } = await import("@/lib/auth/platformApiFetch");
    vi.mocked(platformApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new Request("http://localhost/api/platform/settings/Frontend.BaseUrl/reset", { method: "POST" });
    const response = await POST(request, { params: { key: "Frontend.BaseUrl" } });

    expect(response.status).toBe(401);
  });
});
