import { describe, expect, it, vi } from "vitest";
import { GET } from "./route";

vi.mock("@/lib/auth/apiFetch", () => ({ apiFetch: vi.fn() }));

describe("GET /api/riders/branches/[branchId]/locations", () => {
  it("forwards to the backend branch-locations endpoint", async () => {
    const { apiFetch } = await import("@/lib/auth/apiFetch");
    vi.mocked(apiFetch).mockResolvedValue(
      new Response(JSON.stringify([{ riderId: "r1", latitude: 27.7, longitude: 85.3, accuracyMeters: null, recordedAt: "2026-09-17T00:00:00Z" }]), { status: 200 }),
    );

    const response = await GET(new Request("http://localhost/api/riders/branches/b1/locations"), { params: { branchId: "b1" } });

    expect(response.status).toBe(200);
    expect(apiFetch).toHaveBeenCalledWith("/api/v1/riders/branches/b1/locations");
    const body = await response.json();
    expect(body[0].riderId).toBe("r1");
  });
});
