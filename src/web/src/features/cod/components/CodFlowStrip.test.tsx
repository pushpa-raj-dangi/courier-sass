import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CodFlowStrip } from "./CodFlowStrip";
import { formatRs } from "@/features/cod/lib/codFormat";
import type { CodFlow } from "@/features/cod/types";

const flow: CodFlow = {
  stages: [
    { key: "expected", amount: 1248500, count: 482 },
    { key: "collected", amount: 986200, count: 391 },
    { key: "riderDeposited", amount: 840000, count: 331 },
    { key: "branchReconciled", amount: 795400, count: 314 },
    { key: "financeVerified", amount: 610000, count: 241 },
    { key: "settled", amount: 480300, count: 190 },
  ],
};

describe("CodFlowStrip", () => {
  it("renders six stages with amounts and counts", () => {
    render(<CodFlowStrip flow={flow} isLoading={false} isError={false} onRetry={vi.fn()} onSelectTab={vi.fn()} />);
    expect(screen.getAllByRole("button")).toHaveLength(6);
    expect(screen.getByText(formatRs(1248500))).toBeInTheDocument();
    expect(screen.getByText("482 parcels")).toBeInTheDocument();
    expect(screen.getByText("Settled to merchants")).toBeInTheDocument();
  });

  it("marks the bottleneck stage", () => {
    render(<CodFlowStrip flow={flow} isLoading={false} isError={false} onRetry={vi.fn()} onSelectTab={vi.fn()} />);
    // largest drop is expected → collected (262,300)
    expect(screen.getByRole("button", { name: /Expected/ })).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("button", { name: /Collected/ })).not.toHaveAttribute("aria-current");
  });

  it("switches to the stage's tab on click", () => {
    const onSelectTab = vi.fn();
    render(<CodFlowStrip flow={flow} isLoading={false} isError={false} onRetry={vi.fn()} onSelectTab={onSelectTab} />);
    fireEvent.click(screen.getByRole("button", { name: /Collected/ }));
    expect(onSelectTab).toHaveBeenCalledWith("rider-deposits");
  });

  it("never shows zeros while loading or on error", () => {
    const { rerender } = render(<CodFlowStrip flow={undefined} isLoading isError={false} onRetry={vi.fn()} onSelectTab={vi.fn()} />);
    expect(screen.getByRole("status", { name: "Loading COD lifecycle" })).toBeInTheDocument();
    expect(screen.queryByText(/Rs 0/)).toBeNull();
    const onRetry = vi.fn();
    rerender(<CodFlowStrip flow={undefined} isLoading={false} isError onRetry={onRetry} onSelectTab={vi.fn()} />);
    expect(screen.getByText("Couldn't load the COD flow")).toBeInTheDocument();
    expect(screen.queryByText(/Rs 0/)).toBeNull();
  });
});
