import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { PricingPlans } from "./PricingPlans";
import { PRICING_PLANS } from "../content";

describe("PricingPlans", () => {
  it("renders every plan's name and one pricing card per plan", () => {
    render(<PricingPlans isAuthenticated={false} />);
    for (const plan of PRICING_PLANS) {
      expect(screen.getByText(plan.name)).toBeInTheDocument();
    }
    expect(screen.getAllByTestId("pricing-card")).toHaveLength(PRICING_PLANS.length);
  });

  it("shows NPR prices for numeric plans and Custom for the plan with no fixed price", () => {
    render(<PricingPlans isAuthenticated={false} />);
    const numericPlans = PRICING_PLANS.filter((p) => p.monthlyPriceNpr !== null);
    for (const plan of numericPlans) {
      expect(screen.getByText(`NPR ${plan.monthlyPriceNpr!.toLocaleString("en-US")}`)).toBeInTheDocument();
    }
    expect(screen.getByText("Custom")).toBeInTheDocument();
  });

  it("shows Unlimited for null branch/staff/shipment limits instead of a number", () => {
    render(<PricingPlans isAuthenticated={false} />);
    expect(screen.getAllByText("Unlimited").length).toBeGreaterThan(0);
  });

  it("labels every plan's branch, staff, and shipment metrics for row-by-row comparison", () => {
    render(<PricingPlans isAuthenticated={false} />);
    expect(screen.getAllByText("Branches")).toHaveLength(PRICING_PLANS.length);
    expect(screen.getAllByText("Staff accounts")).toHaveLength(PRICING_PLANS.length);
    expect(screen.getAllByText("Shipments/mo")).toHaveLength(PRICING_PLANS.length);
  });

  it("states the shared 7-day trial once instead of repeating it on every card", () => {
    render(<PricingPlans isAuthenticated={false} />);
    expect(screen.getByText("Every plan includes a 7-day free trial")).toBeInTheDocument();
  });

  it("highlights exactly the default plan with a 'Most teams start here' tag", () => {
    render(<PricingPlans isAuthenticated={false} />);
    const defaultPlan = PRICING_PLANS.find((p) => p.isDefault)!;
    expect(screen.getByText("Most teams start here")).toBeInTheDocument();
    expect(screen.getByText(defaultPlan.name)).toBeInTheDocument();
  });

  it("gives every plan CTA a distinct accessible name", () => {
    render(<PricingPlans isAuthenticated={false} />);
    const links = screen.getAllByRole("link", { name: /Get started — .+ plan/ });
    expect(links).toHaveLength(PRICING_PLANS.length);
    for (const link of links) {
      expect(link).toHaveAttribute("href", "/register");
    }
  });

  it("links every plan CTA to the dashboard when authenticated", () => {
    render(<PricingPlans isAuthenticated />);
    const links = screen.getAllByRole("link", { name: /Go to dashboard — .+ plan/ });
    expect(links).toHaveLength(PRICING_PLANS.length);
    for (const link of links) {
      expect(link).toHaveAttribute("href", "/dashboard");
    }
  });

  it("renders exactly one h1", () => {
    render(<PricingPlans isAuthenticated={false} />);
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
  });

  it("labels the section with a mono eyebrow and keeps plan cards square-cornered (no rounded-xl)", () => {
    const { container } = render(<PricingPlans isAuthenticated={false} />);
    expect(screen.getByText("Plans")).toBeInTheDocument();
    expect(container.querySelector(".rounded-xl")).toBeNull();
  });
});
