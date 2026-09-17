import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { PricingFaq } from "./PricingFaq";
import { PRICING_FAQ } from "../content";

describe("PricingFaq", () => {
  it("renders every FAQ question and answer", () => {
    render(<PricingFaq />);
    for (const faq of PRICING_FAQ) {
      expect(screen.getByText(faq.question)).toBeInTheDocument();
      expect(screen.getByText(faq.answer)).toBeInTheDocument();
    }
  });

  it("renders a section heading", () => {
    render(<PricingFaq />);
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();
  });
});
