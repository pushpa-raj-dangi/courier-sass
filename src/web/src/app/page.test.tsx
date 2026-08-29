import { describe, expect, it, vi } from "vitest";

const { redirectMock } = vi.hoisted(() => ({ redirectMock: vi.fn() }));
vi.mock("next/navigation", () => ({ redirect: redirectMock }));
vi.mock("@/lib/auth/session", () => ({
  getValidAccessToken: vi.fn(),
}));

import RootPage from "./page";
import { getValidAccessToken } from "@/lib/auth/session";

describe("RootPage", () => {
  it("redirects to /dashboard when a session exists", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue("token-123");

    await RootPage();

    expect(redirectMock).toHaveBeenCalledWith("/dashboard");
  });

  it("redirects to /login when there is no session", async () => {
    vi.mocked(getValidAccessToken).mockResolvedValue(null);

    await RootPage();

    expect(redirectMock).toHaveBeenCalledWith("/login");
  });
});
