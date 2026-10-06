import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";

// Flatten the Radix Select so options are inspectable without opening a popover.
vi.mock("@/components/ui/select", () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectValue: () => null,
  SelectContent: ({ children }: { children: ReactNode }) => <ul>{children}</ul>,
  SelectItem: ({ children }: { children: ReactNode }) => <li>{children}</li>,
}));

const perms = new Set<string>();
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => perms.has(p) }));
const useManifests = vi.fn();
vi.mock("@/features/manifests/hooks/useManifests", () => ({ useManifests: (...a: unknown[]) => useManifests(...a) }));
vi.mock("@/features/riders/hooks/useRiders", () => ({
  useRiders: () => ({
    isLoading: false,
    data: [
      { id: "r1", userId: "u1", baseBranchId: "b1", licenseNumber: "L-1", isActive: true },
      { id: "r2", userId: "u2", baseBranchId: "b2", licenseNumber: "L-2", isActive: true },
    ],
  }),
}));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ branchName: (id: string) => id, riderName: () => "Rider" }),
}));

import { ScanContextPicker } from "./ScanContextPicker";
import type { ScanMode } from "@/features/scanning/modes";

const mode = (context: ScanMode["context"]) =>
  ({ id: "receive", label: "x", context, manifestFilter: () => true }) as unknown as ScanMode;

describe("ScanContextPicker", () => {
  beforeEach(() => {
    perms.clear();
    useManifests.mockReset();
    useManifests.mockReturnValue({ data: [], isLoading: false });
  });

  it("renders nothing and does not enable the manifests fetch without manifest.read", () => {
    const { container } = render(<ScanContextPicker mode={mode("manifest")} value={{ kind: "none" }} onChange={vi.fn()} inScope={() => true} />);
    expect(container).toBeEmptyDOMElement();
    expect(useManifests).toHaveBeenCalledWith(undefined, undefined, { enabled: false });
  });

  it("offers the no-manifest option in optional-manifest mode", () => {
    perms.add("manifest.read");
    perms.add("manifest.receive");
    render(<ScanContextPicker mode={mode("optional-manifest")} value={{ kind: "none" }} onChange={vi.fn()} inScope={() => true} />);
    expect(screen.getByText("No manifest — receive at origin")).toBeInTheDocument();
    expect(useManifests).toHaveBeenCalledWith(undefined, undefined, { enabled: true });
  });

  it("filters riders by the inScope predicate and skips manifests in rider mode", () => {
    render(<ScanContextPicker mode={mode("rider")} value={{ kind: "none" }} onChange={vi.fn()} inScope={(b) => b === "b1"} />);
    expect(screen.getByText("Rider · L-1")).toBeInTheDocument();
    expect(screen.queryByText("Rider · L-2")).not.toBeInTheDocument();
    expect(useManifests).toHaveBeenCalledWith(undefined, undefined, { enabled: false });
  });
});
