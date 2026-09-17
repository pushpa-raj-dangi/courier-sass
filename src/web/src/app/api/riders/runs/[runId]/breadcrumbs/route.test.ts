import { describe, expect, it, vi } from "vitest";
import { GET } from "./route";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

describe("GET /api/riders/runs/[runId]/breadcrumbs", () => {
  it("forwards to the backend breadcrumbs endpoint", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockResolvedValue(new Response(JSON.stringify([]), { status: 200 }));

    const response = await GET(new Request("http://localhost/api/riders/runs/run1/breadcrumbs"), { params: { runId: "run1" } });

    expect(response.status).toBe(200);
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/runs/run1/breadcrumbs");
  });
});
