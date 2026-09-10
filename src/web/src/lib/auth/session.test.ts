import { describe, expect, it, vi, beforeEach } from "vitest";

const cookieStore = new Map<string, string>();
const cookieOptionsStore = new Map<string, Record<string, unknown>>();

vi.mock("next/headers", () => ({
  cookies: () => ({
    get: (name: string) => (cookieStore.has(name) ? { value: cookieStore.get(name) } : undefined),
    set: (name: string, value: string, options?: Record<string, unknown>) => {
      cookieStore.set(name, value);
      if (options) {
        cookieOptionsStore.set(name, options);
      }
    },
    delete: (name: string) => {
      cookieStore.delete(name);
      cookieOptionsStore.delete(name);
    },
  }),
}));

import { setSessionCookies, clearSessionCookies, getValidAccessToken } from "./session";

describe("session cookies", () => {
  beforeEach(() => {
    cookieStore.clear();
    cookieOptionsStore.clear();
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

  it("defaults the access-token cookie maxAge to 900 seconds when omitted", () => {
    setSessionCookies("access-123", "refresh-456");

    expect(cookieOptionsStore.get("courier_access_token")?.maxAge).toBe(900);
  });

  it("honors an explicit access-token cookie maxAge when provided", () => {
    setSessionCookies("access-123", "refresh-456", 1800);

    expect(cookieOptionsStore.get("courier_access_token")?.maxAge).toBe(1800);
  });
});
