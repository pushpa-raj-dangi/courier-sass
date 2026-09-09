import { describe, expect, it, vi, beforeEach } from "vitest";

const cookieStore = new Map<string, string>();

vi.mock("next/headers", () => ({
  cookies: () => ({
    get: (name: string) => (cookieStore.has(name) ? { value: cookieStore.get(name) } : undefined),
    set: (name: string, value: string) => {
      cookieStore.set(name, value);
    },
    delete: (name: string) => {
      cookieStore.delete(name);
    },
  }),
}));

import {
  setPlatformSessionCookies,
  clearPlatformSessionCookies,
  getValidPlatformAccessToken,
} from "./platformSession";

describe("platform session cookies", () => {
  beforeEach(() => {
    cookieStore.clear();
  });

  it("returns the stored access token when present", async () => {
    setPlatformSessionCookies("access-123", "refresh-456");

    const token = await getValidPlatformAccessToken();

    expect(token).toBe("access-123");
  });

  it("returns null when there is no session at all", async () => {
    const token = await getValidPlatformAccessToken();

    expect(token).toBeNull();
  });

  it("clears both cookies on logout", async () => {
    setPlatformSessionCookies("access-123", "refresh-456");

    clearPlatformSessionCookies();

    expect(await getValidPlatformAccessToken()).toBeNull();
  });
});
