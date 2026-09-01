import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("GET /api/vehicles", () => {
  it("proxies to the backend and returns the response", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(apiFetch).toHaveBeenCalledWith("/api/v1/vehicles");
    expect(response.status).toBe(200);
  });
});

describe("POST /api/vehicles", () => {
  it("proxies the create request body to the backend", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "v1", plateNumber: "BA 2 KHA 1234" }),
    } as Response);

    const request = new Request("http://localhost/api/vehicles", {
      method: "POST",
      body: JSON.stringify({ type: "Motorcycle", plateNumber: "BA 2 KHA 1234" }),
    });
    const response = await POST(request);

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/vehicles",
      expect.objectContaining({ method: "POST" })
    );
    expect(response.status).toBe(201);
  });
});
