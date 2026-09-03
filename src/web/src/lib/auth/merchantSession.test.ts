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
  setMerchantSessionCookies,
  clearMerchantSessionCookies,
  getValidMerchantAccessToken,
} from "./merchantSession";

describe("merchant session cookies", () => {
  beforeEach(() => {
    cookieStore.clear();
  });

  it("returns the stored access token when present", async () => {
    setMerchantSessionCookies("access-123", "refresh-456");

    const token = await getValidMerchantAccessToken();

    expect(token).toBe("access-123");
  });

  it("returns null when there is no session at all", async () => {
    const token = await getValidMerchantAccessToken();

    expect(token).toBeNull();
  });

  it("clears both cookies on logout", async () => {
    setMerchantSessionCookies("access-123", "refresh-456");

    clearMerchantSessionCookies();

    expect(await getValidMerchantAccessToken()).toBeNull();
  });
});
