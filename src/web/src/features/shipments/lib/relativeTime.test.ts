import { describe, expect, it } from "vitest";
import { formatRelativeTime } from "./relativeTime";

describe("formatRelativeTime", () => {
  const now = new Date("2026-10-05T12:00:00Z");
  it("buckets recent times", () => {
    expect(formatRelativeTime("2026-10-05T11:59:40Z", now)).toBe("just now");
    expect(formatRelativeTime("2026-10-05T11:48:00Z", now)).toBe("12 min ago");
    expect(formatRelativeTime("2026-10-05T09:00:00Z", now)).toBe("3 h ago");
    expect(formatRelativeTime("2026-10-04T08:00:00Z", now)).toBe("Yesterday");
  });
  it("falls back to a date for older times", () => {
    const iso = "2026-09-20T08:00:00Z";
    expect(formatRelativeTime(iso, now)).toBe(new Date(iso).toLocaleDateString());
  });
});
