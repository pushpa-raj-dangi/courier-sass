import { describe, expect, it, vi, beforeEach } from "vitest";

vi.mock("@/lib/env", () => ({ getUpstreamApiUrl: () => "http://api.internal" }));

import { platformAuthService } from "./platformAuthService";

describe("platformAuthService", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("logs in and returns the parsed tokens", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => ({
        accessToken: "access-123",
        refreshToken: "refresh-456",
        userId: "user-1",
      }),
    });

    const tokens = await platformAuthService.login("admin@platform.com", "correct-password");

    expect(global.fetch).toHaveBeenCalledWith(
      "http://api.internal/api/v1/platform/auth/login",
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: "admin@platform.com", password: "correct-password" }),
        cache: "no-store",
      })
    );
    expect(tokens).toEqual({ accessToken: "access-123", refreshToken: "refresh-456", userId: "user-1" });
  });

  it("throws LOGIN_FAILED when the backend rejects the credentials", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: false });

    await expect(platformAuthService.login("admin@platform.com", "wrong-password")).rejects.toThrow(
      "LOGIN_FAILED"
    );
  });

  it("refreshes and returns the parsed tokens", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      json: async () => ({
        accessToken: "access-789",
        refreshToken: "refresh-012",
        userId: "user-1",
      }),
    });

    const tokens = await platformAuthService.refresh("refresh-456");

    expect(global.fetch).toHaveBeenCalledWith(
      "http://api.internal/api/v1/platform/auth/refresh",
      expect.objectContaining({
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: "refresh-456" }),
        cache: "no-store",
      })
    );
    expect(tokens).toEqual({ accessToken: "access-789", refreshToken: "refresh-012", userId: "user-1" });
  });

  it("throws REFRESH_FAILED when the refresh token is invalid", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: false });

    await expect(platformAuthService.refresh("bad-refresh-token")).rejects.toThrow("REFRESH_FAILED");
  });
});
