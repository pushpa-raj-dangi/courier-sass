import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/auth/session", () => ({
  hasSession: vi.fn(),
}));

import RootPage from "./page";
import { hasSession } from "@/lib/auth/session";

describe("RootPage", () => {
  it("renders the marketing page with a Go to dashboard CTA when a session exists", async () => {
    vi.mocked(hasSession).mockReturnValue(true);

    const page = await RootPage();
    render(page);

    expect(screen.getAllByRole("link", { name: /go to dashboard/i }).length).toBeGreaterThan(0);
    expect(screen.queryAllByRole("link", { name: /^sign in$/i })).toHaveLength(0);
  });

  it("renders the marketing page with a Get started CTA and a Sign in link when there is no session", async () => {
    vi.mocked(hasSession).mockReturnValue(false);

    const page = await RootPage();
    render(page);

    expect(screen.getAllByRole("link", { name: /get started/i }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("link", { name: /^sign in$/i }).length).toBeGreaterThan(0);
    expect(screen.queryAllByRole("link", { name: /go to dashboard/i })).toHaveLength(0);
  });

  it("renders exactly one h1 (the hero headline)", async () => {
    vi.mocked(hasSession).mockReturnValue(false);

    const page = await RootPage();
    render(page);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  it("keeps heading levels in order with no skipped levels", async () => {
    vi.mocked(hasSession).mockReturnValue(false);

    const page = await RootPage();
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

    const page = await RootPage();
    render(page);

    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
  });
});
