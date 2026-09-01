import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/riders", () => {
  it("proxies to the backend and returns the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders");
    expect(response.status).toBe(200);
  });
});

describe("POST /api/riders", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "r1", licenseNumber: "LIC-1" }),
    } as Response);

    const request = new Request("http://localhost/api/riders", {
      method: "POST",
      body: JSON.stringify({ email: "a@example.com" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(201);
  });
});
