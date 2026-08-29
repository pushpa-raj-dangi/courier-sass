import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("/api/branches", () => {
  it("GET proxies the branch list", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [{ id: "b1", code: "KTM-01", name: "Kathmandu Hub" }],
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/branches");
    expect(response.status).toBe(200);
  });

  it("POST forwards the request body and proxies a validation error", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ error: { code: "BRANCH_VALIDATION_FAILED", message: "Code is required." } }),
    } as Response);
    const request = new Request("http://localhost/api/branches", {
      method: "POST",
      body: JSON.stringify({ code: "" }),
    });

    const response = await POST(request as never);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/branches",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ code: "" }) })
    );
    expect(response.status).toBe(400);
  });
});
