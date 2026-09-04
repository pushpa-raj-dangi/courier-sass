import { describe, expect, it, vi, beforeEach } from "vitest";

vi.mock("@/lib/env", () => ({ getUpstreamApiUrl: () => "http://api.internal" }));

import { GET } from "./route";

function makeRequest(headers: Record<string, string> = {}) {
  return { headers: new Headers(headers) } as unknown as Request;
}

describe("GET /api/track/[awb]", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("proxies the backend response without any auth header", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({ awb: "AWB1", currentStatus: "Delivered", events: [] }),
    });

    const response = await GET(makeRequest() as never, { params: { awb: "AWB1" } });

    expect(global.fetch).toHaveBeenCalledWith(
      "http://api.internal/track/AWB1",
      expect.objectContaining({ headers: {}, cache: "no-store" })
    );
    expect(response.status).toBe(200);
  });

  it("forwards an inbound X-Forwarded-For header to the backend", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({ awb: "AWB1", currentStatus: "Delivered", events: [] }),
    });

    await GET(makeRequest({ "x-forwarded-for": "203.0.113.5" }) as never, { params: { awb: "AWB1" } });

    expect(global.fetch).toHaveBeenCalledWith(
      "http://api.internal/track/AWB1",
      expect.objectContaining({ headers: { "X-Forwarded-For": "203.0.113.5" } })
    );
  });

  it("passes through a 404 with no body", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 404,
      headers: new Headers(),
      json: async () => {
        throw new Error("no body");
      },
    });

    const response = await GET(makeRequest() as never, { params: { awb: "UNKNOWN" } });

    expect(response.status).toBe(404);
  });

  it("passes through a 429 with its Retry-After header", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 429,
      headers: new Headers({ "Retry-After": "30" }),
      json: async () => ({ error: { code: "RATE_LIMIT_EXCEEDED" } }),
    });

    const response = await GET(makeRequest() as never, { params: { awb: "AWB1" } });

    expect(response.status).toBe(429);
    expect(response.headers.get("Retry-After")).toBe("30");
  });
});
