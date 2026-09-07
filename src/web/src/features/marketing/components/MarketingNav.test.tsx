import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MarketingNav } from "./MarketingNav";

describe("MarketingNav", () => {
  it("shows Sign in linking to /login when unauthenticated", () => {
    render(<MarketingNav isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
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
