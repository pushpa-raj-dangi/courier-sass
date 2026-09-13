import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [] }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/branches/hooks/useCreateBranch", () => ({
  useCreateBranch: () => ({ mutateAsync, isPending: false }),
}));

vi.mock("@/features/geography/hooks/useGeography", () => ({
  useProvinces: () => ({ data: [{ id: "p1", name: "Bagmati" }] }),
  useDistricts: () => ({ data: [] }),
  useLocalLevels: () => ({ data: [] }),
  useMerchantProvinces: () => ({ data: [] }),
  useMerchantDistricts: () => ({ data: [] }),
  useMerchantLocalLevels: () => ({ data: [] }),
}));

let panelMode: "create" | "closed" = "create";
const close = vi.fn(() => {
  panelMode = "closed";
});
const openDetail = vi.fn();
vi.mock("@/features/branches/store/branchPanelStore", () => ({
  useBranchPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: panelMode }, close, openDetail }),
}));

import { BranchCreateSheet } from "./BranchCreateSheet";

describe("BranchCreateSheet", () => {
  it("clears previously entered field values after the sheet is closed without submitting", () => {
    panelMode = "create";
    const { rerender } = render(<BranchCreateSheet />);

    fireEvent.change(screen.getByLabelText("Code"), { target: { value: "KTM-HO" } });
    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Kathmandu Head Office" } });
    expect(screen.getByLabelText("Code")).toHaveValue("KTM-HO");

    // Escape triggers Radix Dialog's onOpenChange(false), which BranchCreateSheet wires into
    // close() + reset() — the underlying component instance (and its useForm state) is not
    // unmounted here, matching how BranchPanelStore actually drives the Sheet's visibility.
    fireEvent.keyDown(document, { key: "Escape", code: "Escape" });
    expect(close).toHaveBeenCalled();

    // Reopen: BranchPanelStore flips back to "create" and the same component instance re-renders.
    panelMode = "create";
    rerender(<BranchCreateSheet />);

    expect(screen.getByLabelText("Code")).toHaveValue("");
    expect(screen.getByLabelText("Name")).toHaveValue("");
  });
});
