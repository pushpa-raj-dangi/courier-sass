import { describe, expect, it, vi } from "vitest";
import { PUT } from "./route";

vi.mock("@/lib/auth/platformApiFetch", () => ({ platformApiFetch: vi.fn() }));

describe("PUT /api/platform/settings/[key]", () => {
  it("forwards a 204 success from the backend without throwing while building the response", async () => {
    const { platformApiFetch } = await import("@/lib/auth/platformApiFetch");
    // The real backend returns 204 No Content on a successful update (PlatformSettingsEndpoints
    // MapPut -> Results.NoContent()). NextResponse.json(body, { status: 204 }) throws because a
    // 204 response cannot carry a body, which the route's own catch then masked as a fake 401.
    vi.mocked(platformApiFetch).mockResolvedValue(new Response(null, { status: 204 }));

    const request = new Request("http://localhost/api/platform/settings/Frontend.BaseUrl", {
      method: "PUT",
      body: JSON.stringify({ value: "https://example.test" }),
    });
    const response = await PUT(request, { params: { key: "Frontend.BaseUrl" } });

    expect(response.status).toBe(204);
  });

  it("forwards a validation error body and status from the backend", async () => {
    const { platformApiFetch } = await import("@/lib/auth/platformApiFetch");
    vi.mocked(platformApiFetch).mockResolvedValue(
      new Response(JSON.stringify({ error: { code: "PLATFORM_SETTING_VALIDATION_FAILED", message: "bad value" } }), {
        status: 400,
      })
    );

    const request = new Request("http://localhost/api/platform/settings/Billing.GraceDays", {
      method: "PUT",
      body: JSON.stringify({ value: "not-a-number" }),
    });
    const response = await PUT(request, { params: { key: "Billing.GraceDays" } });
    const body = await response.json();

    expect(response.status).toBe(400);
    expect(body.error.code).toBe("PLATFORM_SETTING_VALIDATION_FAILED");
  });

  it("returns 401 when platformApiFetch throws (no session)", async () => {
    const { platformApiFetch } = await import("@/lib/auth/platformApiFetch");
    vi.mocked(platformApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const request = new Request("http://localhost/api/platform/settings/Frontend.BaseUrl", {
      method: "PUT",
      body: JSON.stringify({ value: "https://example.test" }),
    });
    const response = await PUT(request, { params: { key: "Frontend.BaseUrl" } });

    expect(response.status).toBe(401);
  });
});
