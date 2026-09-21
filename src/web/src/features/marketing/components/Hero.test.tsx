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

  it("links the secondary CTA to the custody section anchor", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "See how it works" })).toHaveAttribute("href", "#custody");
  });

  it("shows the real dashboard screenshot as the hero visual", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("img", { name: dashboardScreenshot.alt })).toHaveAttribute("src", dashboardScreenshot.src);
  });

  it("layers labelled sample-data cards over the screenshot", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getAllByText("Sample data").length).toBeGreaterThanOrEqual(3);
    expect(screen.getByText(/NPR 2,350/)).toBeInTheDocument();
  });

  it("renders the decorative grid background", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByTestId("marketing-grid")).toHaveAttribute("aria-hidden", "true");
  });
});
