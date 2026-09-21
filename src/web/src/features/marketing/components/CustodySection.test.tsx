import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CustodySection } from "./CustodySection";

describe("CustodySection", () => {
  it("is the #custody anchor with a labelled h2 and the custody-chain figure", () => {
    const { container } = render(<CustodySection />);
    expect(container.querySelector("section#custody")).not.toBeNull();
    expect(screen.getByRole("heading", { level: 2 })).toBeInTheDocument();
    expect(screen.getByRole("figure", { name: /custody chain/i })).toBeInTheDocument();
  });
});
