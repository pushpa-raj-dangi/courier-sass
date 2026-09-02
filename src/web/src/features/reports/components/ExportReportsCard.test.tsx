import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub" }] }),
}));

const mockUsePermission = vi.fn();
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: () => mockUsePermission(),
}));

import { ExportReportsCard } from "./ExportReportsCard";

// DatePicker's Calendar opens on the current month by default — pinning system time keeps the
// visible month (and thus which [data-day] cells are on-screen without extra nav-button clicks)
// deterministic regardless of when this suite runs.
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 7, 15));
});
afterEach(() => {
  vi.useRealTimers();
});

// react-day-picker's own <td data-day="yyyy-MM-dd"> is locale-independent; the inner button's own
// data-day (CalendarDayButton, calendar.tsx) uses toLocaleDateString() and isn't safe to match on.
function pickCalendarDate(triggerLabel: string, isoDate: string) {
  fireEvent.click(screen.getByLabelText(triggerLabel));
  const cell = document.querySelector(`td[data-day="${isoDate}"]`);
  const button = cell?.querySelector("button");
  if (!button) throw new Error(`No calendar day button found for ${isoDate}`);
  fireEvent.click(button);
}

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
    pickCalendarDate("Export from date", "2026-08-01");
    pickCalendarDate("Export to date", "2026-08-31");

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
