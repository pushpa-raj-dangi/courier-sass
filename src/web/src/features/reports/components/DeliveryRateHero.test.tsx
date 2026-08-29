import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { DeliveryRateHero } from "./DeliveryRateHero";

describe("DeliveryRateHero", () => {
  it("renders the rate rounded to a whole percent", () => {
    render(<DeliveryRateHero rate={0.7555} />);
    expect(screen.getByText("76%")).toBeInTheDocument();
  });

  it("clamps a rate above 1 to 100%", () => {
    render(<DeliveryRateHero rate={1.4} />);
    expect(screen.getByText("100%")).toBeInTheDocument();
  });

  it("shows an em dash for a null rate instead of 0%", () => {
    render(<DeliveryRateHero rate={null} />);
    expect(screen.getByText("—")).toBeInTheDocument();
  });
});
