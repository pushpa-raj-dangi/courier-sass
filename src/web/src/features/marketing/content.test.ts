import { describe, expect, it } from "vitest";
import {
  getPrimaryCta,
  FEATURES,
  HOW_IT_WORKS_STEPS,
  WALKTHROUGH_STOPS,
  CREDIBILITY_POINTS,
  FOOTER_LINKS,
  NAV_LINKS,
  SCREENSHOTS,
  PRICING_PLANS,
  PRICING_FAQ,
} from "./content";

describe("getPrimaryCta", () => {
  it("returns the get-started CTA when unauthenticated", () => {
    expect(getPrimaryCta(false)).toEqual({ href: "/register", label: "Get started" });
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
    expect(hrefs).toEqual(["/register", "/login", "/merchant-login", "/track"]);
  });

  it("nav links include the public tracking route", () => {
    expect(NAV_LINKS.some((l) => l.href === "/track")).toBe(true);
  });

  it("nav links include the pricing route", () => {
    expect(NAV_LINKS.some((l) => l.href === "/pricing")).toBe(true);
  });

  it("has exactly 4 screenshots, each with a src under /marketing/screenshots, non-empty alt text, and a caption", () => {
    expect(SCREENSHOTS).toHaveLength(4);
    const ids = SCREENSHOTS.map((s) => s.id);
    expect(ids).toEqual(["dashboard", "shipment-detail", "scan-manifest", "cod-settlement"]);
    for (const screenshot of SCREENSHOTS) {
      expect(screenshot.src).toMatch(/^\/marketing\/screenshots\/.+\.webp$/);
      expect(screenshot.alt.length).toBeGreaterThan(0);
      expect(screenshot.caption.length).toBeGreaterThan(0);
    }
  });

  it("has exactly 4 pricing plans, ordered cheapest to most expensive with exactly one default", () => {
    expect(PRICING_PLANS).toHaveLength(4);
    const priced = PRICING_PLANS.filter((p): p is typeof p & { monthlyPriceNpr: number } => p.monthlyPriceNpr !== null);
    const prices = priced.map((p) => p.monthlyPriceNpr);
    expect(prices).toEqual([...prices].sort((a, b) => a - b));
    expect(PRICING_PLANS.filter((p) => p.isDefault)).toHaveLength(1);
  });

  it("gives every pricing plan a unique slug and a non-negative or null price", () => {
    const slugs = PRICING_PLANS.map((p) => p.slug);
    expect(new Set(slugs).size).toBe(slugs.length);
    for (const plan of PRICING_PLANS) {
      expect(plan.monthlyPriceNpr === null || plan.monthlyPriceNpr >= 0).toBe(true);
    }
  });

  it("has at least 3 pricing FAQ entries, each with a question and answer", () => {
    expect(PRICING_FAQ.length).toBeGreaterThanOrEqual(3);
    for (const faq of PRICING_FAQ) {
      expect(faq.question.length).toBeGreaterThan(0);
      expect(faq.answer.length).toBeGreaterThan(0);
    }
  });
});
