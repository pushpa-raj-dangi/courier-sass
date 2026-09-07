import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { FinalCta } from "./FinalCta";

describe("FinalCta", () => {
  it("links to /login when unauthenticated", () => {
    render(<FinalCta isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
  });

  it("links to /dashboard when authenticated", () => {
    render(<FinalCta isAuthenticated />);
    expect(screen.getByRole("link", { name: /go to dashboard/i })).toHaveAttribute("href", "/dashboard");
  });

  it("keeps the CTA link functional after the glow styling pass", () => {
    render(<FinalCta isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
  });
});
