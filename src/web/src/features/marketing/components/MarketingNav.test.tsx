import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MarketingNav } from "./MarketingNav";

describe("MarketingNav", () => {
  it("shows Get started linking to /register when unauthenticated", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "Get started" })).toHaveAttribute("href", "/register");
  });

  it("shows a secondary Sign in link to /login when unauthenticated", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
  });

  it("hides the Sign in link when authenticated", () => {
    render(<MarketingNav isAuthenticated />);
    expect(screen.queryByRole("link", { name: "Sign in" })).not.toBeInTheDocument();
  });

  it("shows Go to dashboard linking to /dashboard when authenticated", () => {
    render(<MarketingNav isAuthenticated />);
    expect(screen.getByRole("link", { name: "Go to dashboard" })).toHaveAttribute("href", "/dashboard");
  });

  it("renders the Track a parcel link to /track", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "Track a parcel" })).toHaveAttribute("href", "/track");
  });

  it("renders the Courier Platform wordmark", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByText("Courier Platform")).toBeInTheDocument();
  });
});
