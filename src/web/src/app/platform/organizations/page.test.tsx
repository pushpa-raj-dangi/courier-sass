import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

const organizations = [
  { id: "org-1", name: "Kathmandu Couriers", slug: "kathmandu-couriers", planName: "Growth", isActive: true, isDeleted: false, branchCount: 3 },
  { id: "org-2", name: "Suspended Co", slug: "suspended-co", planName: null, isActive: false, isDeleted: false, branchCount: 1 },
];
const pagedResult = { items: organizations, page: 1, pageSize: 25, totalCount: 2, totalPages: 1 };

const usePlatformOrganizations = vi.fn(() => ({ data: pagedResult, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-organizations/hooks/usePlatformOrganizations", () => ({
  usePlatformOrganizations: (...args: unknown[]) => usePlatformOrganizations(...(args as [])),
}));

const openDetail = vi.fn();
vi.mock("@/features/platform-organizations/store/organizationPanelStore", () => ({
  useOrganizationPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "closed" }, openDetail, close: vi.fn() }),
}));

vi.mock("@/features/platform-organizations/OrganizationDetailSheet", () => ({
  OrganizationDetailSheet: () => null,
}));

import PlatformOrganizationsPage from "./page";

describe("PlatformOrganizationsPage", () => {
  it("renders the organization list and opens the detail sheet on row click", () => {
    render(<PlatformOrganizationsPage />);

    expect(screen.getByText("Kathmandu Couriers")).toBeInTheDocument();
    expect(screen.getByText("Suspended Co")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Suspended")).toBeInTheDocument();

    fireEvent.click(screen.getByText("Kathmandu Couriers"));
    expect(openDetail).toHaveBeenCalledWith("org-1");
  });

  it("passes the debounced search text to the hook after typing", async () => {
    render(<PlatformOrganizationsPage />);
    usePlatformOrganizations.mockClear();

    fireEvent.change(screen.getByLabelText("Search organizations"), { target: { value: "kathmandu" } });

    await waitFor(() => {
      expect(usePlatformOrganizations).toHaveBeenCalledWith("kathmandu", "all", 1, 25);
    }, { timeout: 1000 });
  });

  // A full select-and-click interaction test for shadcn's Select requires simulating Radix's
  // portal-based listbox, which doesn't reliably open under jsdom in this codebase (same limit
  // already documented for OrganizationDetailSheet's Assign Plan control) — this confirms the
  // status filter control renders with the expected default, rather than the click interaction.
  it("renders the status filter defaulted to All statuses", () => {
    render(<PlatformOrganizationsPage />);

    expect(screen.getByLabelText("Filter by status")).toBeInTheDocument();
    expect(screen.getByText("All statuses")).toBeInTheDocument();
  });

  it("passes the selected page to the hook via DataTable's onPageChange", async () => {
    usePlatformOrganizations.mockReturnValue({
      data: { items: organizations, page: 1, pageSize: 1, totalCount: 2, totalPages: 2 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    render(<PlatformOrganizationsPage />);
    usePlatformOrganizations.mockClear();

    fireEvent.click(screen.getByText("2"));

    await waitFor(() => {
      expect(usePlatformOrganizations).toHaveBeenCalledWith("", "all", 2, 25);
    });
  });
});
