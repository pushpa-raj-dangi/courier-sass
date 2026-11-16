import { describe, expect, it } from "vitest";
import { branchPinHtml, escapeHtml, riderPinHtml } from "./mapPins";

const base = { riderId: "r1", latitude: 27.7, longitude: 85.3, initials: "AG", label: "Asha Gurung", minutesAgo: 3, popup: null };

describe("map pins", () => {
  it("draws a fresh ping as a solid disc without an age label", () => {
    const html = riderPinHtml({ ...base, stale: false });
    expect(html).toContain("bg-primary");
    expect(html).toContain(">AG<");
    expect(html).not.toContain("min<");
  });

  it("draws a stale ping dashed with its age in words", () => {
    const html = riderPinHtml({ ...base, stale: true, minutesAgo: 42 });
    expect(html).toContain("border-dashed");
    expect(html).toContain(">42 min<");
  });

  it("escapes text placed into marker HTML", () => {
    expect(escapeHtml(`<b>"x" & 'y'</b>`)).toBe("&lt;b&gt;&quot;x&quot; &amp; &#39;y&#39;&lt;/b&gt;");
    expect(branchPinHtml("KTM<1>")).toContain("KTM&lt;1&gt;");
  });
});
