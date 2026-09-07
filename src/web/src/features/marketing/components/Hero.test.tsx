import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Hero } from "./Hero";

describe("Hero", () => {
  it("renders the headline as the page's one h1", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent("Every parcel, accounted for.");
  });

  it("links the primary CTA to /login when unauthenticated", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute("href", "/login");
  });

  it("links the primary CTA to /dashboard when authenticated", () => {
    render(<Hero isAuthenticated />);
    expect(screen.getByRole("link", { name: /go to dashboard/i })).toHaveAttribute("href", "/dashboard");
  });

  it("links the secondary CTA to the walkthrough anchor", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByRole("link", { name: "See how it works" })).toHaveAttribute("href", "#walkthrough");
  });

  it("shows the hero status preview chip", () => {
    render(<Hero isAuthenticated={false} />);
    expect(screen.getByTestId("hero-status-preview")).toHaveTextContent("Created");
  });
});
