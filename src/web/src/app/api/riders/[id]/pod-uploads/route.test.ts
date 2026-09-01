import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/apiFetch", () => ({
  apiFetch: vi.fn(),
}));

import { POST } from "./route";
import { apiFetch } from "@/lib/auth/apiFetch";

describe("POST /api/riders/[id]/pod-uploads", () => {
  it("forwards the raw body and content-type, with kind as a query param", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ storageKey: "pod/Signature/r1/abc" }),
    } as Response);

    const request = new Request("http://localhost/api/riders/r1/pod-uploads?kind=Signature", {
      method: "POST",
      headers: { "Content-Type": "image/png" },
      body: new Uint8Array([1, 2, 3, 4]),
    });
    const response = await POST(request, { params: { id: "r1" } });

    expect(apiFetch).toHaveBeenCalledWith(
      "/api/v1/riders/r1/pod-uploads?kind=Signature",
      expect.objectContaining({ method: "POST", headers: { "Content-Type": "image/png" } })
    );
    expect(response.status).toBe(200);
  });
});
