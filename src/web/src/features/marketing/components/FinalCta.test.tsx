import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { FinalCta } from "./FinalCta";

describe("FinalCta", () => {
  it("links to /register when unauthenticated", () => {
    render(<FinalCta isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /get started/i })).toHaveAttribute("href", "/register");
  });

  it("links to /dashboard when authenticated", () => {
    render(<FinalCta isAuthenticated />);
    expect(screen.getByRole("link", { name: /go to dashboard/i })).toHaveAttribute("href", "/dashboard");
  });

  it("keeps the CTA link functional after the glow styling pass", () => {
    render(<FinalCta isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /get started/i })).toHaveAttribute("href", "/register");
  });

  it("offers a secondary Track a parcel link to /track", () => {
    render(<FinalCta isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "Track a parcel" })).toHaveAttribute("href", "/track");
  });
});
