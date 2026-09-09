import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const metrics = {
  totalOrganizations: 10,
  activeOrganizations: 7,
  suspendedOrganizations: 2,
  deletedOrganizations: 1,
  monthlyRecurringRevenueNpr: 45000,
  overdueInvoiceCount: 3,
};

vi.mock("@/features/platform-dashboard/hooks/usePlatformDashboard", () => ({
  usePlatformDashboard: () => ({ data: metrics, isLoading: false, isError: false, refetch: vi.fn() }),
}));

import PlatformDashboardPage from "./page";

describe("PlatformDashboardPage", () => {
  it("renders the platform metrics stat tiles", () => {
    render(<PlatformDashboardPage />);

    expect(screen.getByText("Platform Dashboard")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("7")).toBeInTheDocument();
    expect(screen.getByText("45000")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
  });
});
