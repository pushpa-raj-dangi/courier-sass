import { describe, expect, it, vi } from "vitest";
import { NextRequest } from "next/server";

vi.mock("@/lib/auth/merchantApiFetch", () => ({
  merchantApiFetch: vi.fn(),
}));

import { GET, POST } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/staff", () => {
  it("proxies the response from the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [],
    } as Response);

    const response = await GET();

    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/merchant/staff");
    expect(response.status).toBe(200);
  });

  // Regression test for the Task 16 E2E-discovered bug: a Staff-role session hitting this
  // Owner-only route gets a genuine 403 Forbidden from the backend with an empty body (bare
  // Results.Forbid(), Content-Length: 0) — `await response.json()` throws SyntaxError on that
  // empty input, which the outer catch used to swallow into a misleading generic 401, hiding the
  // real 403 from the client. The fix (`.catch(() => null)` on the json parse) must preserve the
  // backend's actual status code.
  it("preserves the backend's 403 status when the error response has an empty body", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => {
        throw new SyntaxError("Unexpected end of JSON input");
      },
    } as unknown as Response);

    const response = await GET();

    expect(response.status).toBe(403);
  });

  it("returns 401 when merchantApiFetch throws NOT_AUTHENTICATED", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));

    const response = await GET();

    expect(response.status).toBe(401);
  });
});

describe("POST /api/merchant/staff", () => {
  it("proxies the invite request body to the backend", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => ({ id: "su1" }),
    } as Response);

    const request = new NextRequest("http://localhost/api/merchant/staff", {
      method: "POST",
      body: JSON.stringify({ email: "staff@example.com" }),
    });
    const response = await POST(request);

    expect(merchantApiFetch).toHaveBeenCalledWith(
      "/api/v1/merchant/staff",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ email: "staff@example.com" }) })
    );
    expect(response.status).toBe(201);
  });

  it("preserves the backend's 403 status when the error response has an empty body", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => {
        throw new SyntaxError("Unexpected end of JSON input");
      },
    } as unknown as Response);

    const request = new NextRequest("http://localhost/api/merchant/staff", {
      method: "POST",
      body: JSON.stringify({ email: "staff@example.com" }),
    });
    const response = await POST(request);

    expect(response.status).toBe(403);
  });
});
