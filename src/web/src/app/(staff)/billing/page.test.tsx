import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const usePermission = vi.fn();
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: (...args: unknown[]) => usePermission(...args),
}));

const useSubscriptionStatus = vi.fn();
vi.mock("@/features/billing/hooks/useSubscriptionStatus", () => ({
  useSubscriptionStatus: () => useSubscriptionStatus(),
}));

const useBillingInvoices = vi.fn();
vi.mock("@/features/billing/hooks/useBillingInvoices", () => ({
  useBillingInvoices: () => useBillingInvoices(),
}));

vi.mock("@/features/billing/hooks/usePayInvoice", () => ({
  usePayInvoice: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const useAvailablePlans = vi.fn();
vi.mock("@/features/billing/hooks/useAvailablePlans", () => ({
  useAvailablePlans: () => useAvailablePlans(),
}));

vi.mock("@/features/billing/hooks/useAssignPlan", () => ({
  useAssignPlan: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

import BillingPage from "./page";

describe("BillingPage", () => {
  it("shows a non-owner message instead of billing data when the user lacks org.billing.manage", () => {
    usePermission.mockReturnValue(false);
    useSubscriptionStatus.mockReturnValue({ data: undefined, isLoading: false });
    useBillingInvoices.mockReturnValue({ data: undefined, isLoading: false, isError: false, refetch: vi.fn() });
    useAvailablePlans.mockReturnValue({ data: undefined, isLoading: false });

    render(<BillingPage />);

    expect(screen.getByText(/billing is managed by your organization owner/i)).toBeInTheDocument();
    expect(screen.queryByText(/choose this plan/i)).not.toBeInTheDocument();
  });

  it("shows a destructive alert and the plan picker when the trial has expired, for an owner", () => {
    usePermission.mockReturnValue(true);
    useSubscriptionStatus.mockReturnValue({
      data: { status: "TrialExpired", trialEndsAt: null, planName: null, monthlyPriceNpr: null },
      isLoading: false,
    });
    useBillingInvoices.mockReturnValue({ data: [], isLoading: false, isError: false, refetch: vi.fn() });
    useAvailablePlans.mockReturnValue({
      data: [{ id: "p1", name: "Growth", slug: "growth", monthlyPriceNpr: 1500, maxBranches: null, maxStaffUsers: null, maxMonthlyShipments: null, isDefault: true, isActive: true }],
      isLoading: false,
    });

    render(<BillingPage />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent(/your trial has ended/i);
    expect(alert).toHaveTextContent(/choose a plan below to continue/i);
    expect(screen.getByRole("button", { name: /choose this plan/i })).toBeInTheDocument();
  });
});
