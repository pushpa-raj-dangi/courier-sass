import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

let granted: string[] = [];
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => granted.includes(p) }));

import { NdrRtoSwitch } from "./NdrRtoSwitch";

describe("NdrRtoSwitch", () => {
  it("shows both tabs with the active one selected when both reads are allowed", () => {
    granted = ["ndr.read", "rto.read"];
    render(<NdrRtoSwitch active="returns" />);
    expect(screen.getByRole("tab", { name: "Returns" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "NDR queue" })).toHaveAttribute("href", "/ndrs");
  });

  it("renders nothing when only one side is permitted", () => {
    granted = ["ndr.read"];
    const { container } = render(<NdrRtoSwitch active="ndr" />);
    expect(container).toBeEmptyDOMElement();
  });
});
