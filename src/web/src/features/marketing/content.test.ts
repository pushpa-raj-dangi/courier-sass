import { describe, expect, it } from "vitest";
import {
  getPrimaryCta,
  FEATURES,
  HOW_IT_WORKS_STEPS,
  WALKTHROUGH_STOPS,
  CREDIBILITY_POINTS,
  FOOTER_LINKS,
  NAV_LINKS,
} from "./content";

describe("getPrimaryCta", () => {
  it("returns the sign-in CTA when unauthenticated", () => {
    expect(getPrimaryCta(false)).toEqual({ href: "/login", label: "Sign in" });
  });

  it("returns the dashboard CTA when authenticated", () => {
    expect(getPrimaryCta(true)).toEqual({ href: "/dashboard", label: "Go to dashboard" });
  });
});

describe("marketing content data", () => {
  it("has exactly 6 features, all with an icon, title, and description", () => {
    expect(FEATURES).toHaveLength(6);
    for (const feature of FEATURES) {
      expect(feature.icon).toBeDefined();
      expect(feature.title.length).toBeGreaterThan(0);
      expect(feature.description.length).toBeGreaterThan(0);
    }
  });

  it("has exactly 4 how-it-works steps, numbered 01-04", () => {
    expect(HOW_IT_WORKS_STEPS.map((s) => s.step)).toEqual(["01", "02", "03", "04"]);
  });

  it("has exactly 4 walkthrough stops ending in Delivered", () => {
    expect(WALKTHROUGH_STOPS).toHaveLength(4);
    expect(WALKTHROUGH_STOPS[3].label).toBe("Delivered");
    expect(WALKTHROUGH_STOPS[3].tone).toBe("success");
  });

  it("has at least 4 credibility points and none reference customers, logos, or numeric usage stats", () => {
    expect(CREDIBILITY_POINTS.length).toBeGreaterThanOrEqual(4);
    const bannedWords = /customer|testimonial|clients?|logo|\d+\s*(deliveries|shipments|companies|users)/i;
    for (const point of CREDIBILITY_POINTS) {
      expect(point.title).not.toMatch(bannedWords);
      expect(point.description).not.toMatch(bannedWords);
    }
  });

  it("footer links point at real existing routes only", () => {
    const hrefs = FOOTER_LINKS.map((l) => l.href);
    expect(hrefs).toEqual(["/login", "/merchant-login", "/track"]);
  });

  it("nav links include the public tracking route", () => {
    expect(NAV_LINKS.some((l) => l.href === "/track")).toBe(true);
  });
});
