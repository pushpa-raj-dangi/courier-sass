import { describe, expect, it, afterEach } from "vitest";
import { readImpersonationMeta } from "@/lib/impersonation";

describe("readImpersonationMeta", () => {
  afterEach(() => {
    document.cookie = "courier_impersonation_meta=; max-age=0; path=/";
  });

  it("returns null when no cookie is present", () => {
    expect(readImpersonationMeta()).toBeNull();
  });

  it("parses a present cookie", () => {
    const meta = { name: "Jane Owner", organizationName: "Acme Courier", expiresAt: new Date(Date.now() + 60000).toISOString() };
    document.cookie = `courier_impersonation_meta=${encodeURIComponent(JSON.stringify(meta))}; path=/`;

    expect(readImpersonationMeta()).toEqual(meta);
  });

  it("returns null for malformed cookie content", () => {
    document.cookie = "courier_impersonation_meta=not-json; path=/";

    expect(readImpersonationMeta()).toBeNull();
  });
});
