import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }),
}));

const mockUsePermission = vi.fn();
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: () => mockUsePermission(),
}));

import { ExportReportsCard } from "./ExportReportsCard";

describe("ExportReportsCard", () => {
  it("renders nothing when the caller lacks report.export", () => {
    mockUsePermission.mockReturnValue(false);
    const { container } = render(<ExportReportsCard />);
    expect(container).toBeEmptyDOMElement();
  });

  it("builds export links with the selected branch and UTC-anchored dates", () => {
    mockUsePermission.mockReturnValue(true);
    render(<ExportReportsCard />);

    fireEvent.click(screen.getByLabelText("Filter by branch"));
    fireEvent.click(screen.getByRole("option", { name: "Kathmandu Hub" }));
    fireEvent.change(screen.getByLabelText("Export from date"), { target: { value: "2026-08-01" } });
    fireEvent.change(screen.getByLabelText("Export to date"), { target: { value: "2026-08-31" } });

    const shipmentsLink = screen.getByRole("link", { name: /export shipments/i });
    expect(shipmentsLink).toHaveAttribute(
      "href",
      "/api/reports/export/shipments?branchId=b1&from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z"
    );
    const codLink = screen.getByRole("link", { name: /export cod summary/i });
    expect(codLink).toHaveAttribute(
      "href",
      "/api/reports/export/cod-summary?branchId=b1&from=2026-08-01T00%3A00%3A00Z&to=2026-08-31T23%3A59%3A59Z"
    );
  });
});
