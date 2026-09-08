import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Hero } from "./Hero";
import { SCREENSHOTS } from "../content";

const dashboardScreenshot = SCREENSHOTS.find((s) => s.id === "dashboard")!;

describe("Hero", () => {
  it("renders the headline as the page's one h1", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Every parcel, accounted for.");
  });

  it("links the primary CTA to /register when unauthenticated", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /get started/i })).toHaveAttribute("href", "/register");
  });

  it("links the primary CTA to /dashboard when authenticated", () => {
    render(<Hero isAuthenticated />);
    expect(screen.getByRole("link", { name: /go to dashboard/i })).toHaveAttribute("href", "/dashboard");
  });

  it("links the secondary CTA to the walkthrough anchor", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "See how it works" })).toHaveAttribute("href", "#walkthrough");
  });

  it("shows the real dashboard screenshot as the hero visual", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("img", { name: dashboardScreenshot.alt })).toHaveAttribute(
      "src",
      dashboardScreenshot.src
    );
  });

  it("renders the decorative gradient orb and route-line graphic", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByTestId("hero-gradient-orb")).toHaveAttribute("aria-hidden", "true");
    expect(screen.getByTestId("route-line-graphic")).toBeInTheDocument();
  });
});
