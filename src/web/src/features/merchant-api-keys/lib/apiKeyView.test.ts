import { describe, expect, it } from "vitest";
import { formatDate } from "@/features/cod/lib/codFormat";
import { isRecentlyUsed, lastUsedLabel, splitKeys } from "./apiKeyView";
import type { ApiKey } from "../types";

const now = new Date("2026-11-18T10:00:00Z");
const ago = (ms: number) => new Date(now.getTime() - ms).toISOString();
const MIN = 60_000;
const H = 60 * MIN;

describe("lastUsedLabel", () => {
  it("says Never for null", () => expect(lastUsedLabel(null, now)).toBe("Never"));
  it("uses minutes under an hour", () => expect(lastUsedLabel(ago(12 * MIN), now)).toBe("12 min ago"));
  it("uses hours under a day", () => expect(lastUsedLabel(ago(3 * H), now)).toBe("3 h ago"));
  it("falls back to a date after a day", () => {
    const iso = ago(48 * H);
    expect(lastUsedLabel(iso, now)).toBe(formatDate(iso));
  });
});

describe("isRecentlyUsed", () => {
  it("is true within 24 h", () => expect(isRecentlyUsed(ago(23 * H), now)).toBe(true));
  it("is false beyond 24 h", () => expect(isRecentlyUsed(ago(25 * H), now)).toBe(false));
  it("is false when never used", () => expect(isRecentlyUsed(null, now)).toBe(false));
});

describe("splitKeys", () => {
  it("splits by isActive, newest first", () => {
    const k = (id: string, isActive: boolean, createdAt: string): ApiKey => ({
      id, prefix: id, scopes: [], isActive, createdAt, revokedAt: null, lastUsedAt: null,
    });
    const { active, revoked } = splitKeys([
      k("a", true, "2026-01-01T00:00:00Z"),
      k("b", false, "2026-02-01T00:00:00Z"),
      k("c", true, "2026-03-01T00:00:00Z"),
      k("d", false, "2026-04-01T00:00:00Z"),
    ]);
    expect(active.map((x) => x.id)).toEqual(["c", "a"]);
    expect(revoked.map((x) => x.id)).toEqual(["d", "b"]);
  });
});
