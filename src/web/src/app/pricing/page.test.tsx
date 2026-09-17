import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/auth/session", () => ({
  hasSession: vi.fn(),
}));

import PricingPage from "./page";
import { hasSession } from "@/lib/auth/session";
import { PRICING_PLANS } from "@/features/marketing/content";

describe("PricingPage", () => {
  it("renders one pricing card per plan", async () => {
    vi.mocked(hasSession).mockReturnValue(false);

    const page = await PricingPage();
    render(page);

    expect(screen.getAllByTestId("pricing-card")).toHaveLength(PRICING_PLANS.length);
  });

  it("renders exactly one h1 (the pricing headline)", async () => {
    vi.mocked(hasSession).mockReturnValue(false);

    const page = await PricingPage();
    render(page);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  it("keeps heading levels in order with no skipped levels", async () => {
    vi.mocked(hasSession).mockReturnValue(false);

    const page = await PricingPage();
    render(page);

    const levels = screen.getAllByRole("heading").map((heading) => Number(heading.tagName.substring(1)));
    let maxSeen = levels[0];
    for (const level of levels) {
      if (level > maxSeen) {
        expect(level - maxSeen).toBeLessThanOrEqual(1);
      }
      maxSeen = Math.max(maxSeen, level);
    }
  });

  it("exposes header, main, and footer landmarks", async () => {
    vi.mocked(hasSession).mockReturnValue(false);

    const page = await PricingPage();
    render(page);

    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
  });

  it("shows Go to dashboard CTAs when a session exists", async () => {
    vi.mocked(hasSession).mockReturnValue(true);

    const page = await PricingPage();
    render(page);

    expect(screen.getAllByRole("link", { name: /go to dashboard/i }).length).toBeGreaterThan(0);
  });
});
