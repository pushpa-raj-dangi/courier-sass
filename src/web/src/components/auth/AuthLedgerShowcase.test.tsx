import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { AuthLedgerShowcase } from "./AuthLedgerShowcase";

describe("AuthLedgerShowcase", () => {
  it("renders the headline and a labelled sample custody ledger ending in the COD math", () => {
    render(<AuthLedgerShowcase icon={Package} brand="Courier Platform" headline="Headline" description="Desc" />);
    expect(screen.getByRole("heading", { name: "Headline" })).toBeInTheDocument();
    expect(screen.getByText(/Sample shipment · illustrative/)).toBeInTheDocument();
    expect(screen.getAllByRole("listitem").length).toBeGreaterThanOrEqual(5);
    expect(screen.getByText("NPR 2,350")).toBeInTheDocument();
  });
});
