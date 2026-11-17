import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/merchantApiFetch", () => ({ merchantApiFetch: vi.fn() }));

import { GET } from "./route";
import { merchantApiFetch } from "@/lib/auth/merchantApiFetch";

describe("GET /api/merchant/cod/settlements/[id]/export", () => {
  it("streams the CSV and preserves content headers", async () => {
    vi.mocked(merchantApiFetch).mockResolvedValue(
      new Response("awb,net\nA1,100\n", {
        status: 200,
        headers: { "Content-Type": "text/csv", "Content-Disposition": 'attachment; filename="s.csv"' },
      })
    );
    const response = await GET(new Request("http://localhost"), { params: { id: "s1" } });
    expect(merchantApiFetch).toHaveBeenCalledWith("/api/v1/cod/settlements/s1/export");
    expect(response.status).toBe(200);
    expect(response.headers.get("content-disposition")).toBe('attachment; filename="s.csv"');
    expect(response.headers.get("content-type")).toContain("text/csv");
    expect(await response.text()).toContain("A1,100");
  });

  it("returns 401 when merchantApiFetch throws", async () => {
    vi.mocked(merchantApiFetch).mockRejectedValue(new Error("NOT_AUTHENTICATED"));
    const response = await GET(new Request("http://localhost"), { params: { id: "s1" } });
    expect(response.status).toBe(401);
  });
});
