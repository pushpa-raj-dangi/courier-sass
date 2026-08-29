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

import { setSessionCookies, clearSessionCookies, getValidAccessToken } from "./session";

describe("session cookies", () => {
  beforeEach(() => {
    cookieStore.clear();
  });

  it("returns the stored access token when present", async () => {
    setSessionCookies("access-123", "refresh-456");

    const token = await getValidAccessToken();

    expect(token).toBe("access-123");
  });

  it("returns null when there is no session at all", async () => {
    const token = await getValidAccessToken();

    expect(token).toBeNull();
  });

  it("clears both cookies on logout", async () => {
    setSessionCookies("access-123", "refresh-456");

    clearSessionCookies();

    expect(await getValidAccessToken()).toBeNull();
  });
});
