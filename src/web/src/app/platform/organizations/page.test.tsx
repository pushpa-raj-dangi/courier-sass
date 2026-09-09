import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

const organizations = [
  { id: "org-1", name: "Kathmandu Couriers", slug: "kathmandu-couriers", planName: "Growth", isActive: true, isDeleted: false, branchCount: 3 },
  { id: "org-2", name: "Suspended Co", slug: "suspended-co", planName: null, isActive: false, isDeleted: false, branchCount: 1 },
];

vi.mock("@/features/platform-organizations/hooks/usePlatformOrganizations", () => ({
  usePlatformOrganizations: () => ({ data: organizations, isLoading: false, isError: false, refetch: vi.fn() }),
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
});
