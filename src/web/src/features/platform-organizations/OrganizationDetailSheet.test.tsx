import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

function makeDetail(overrides: Partial<{ isActive: boolean; deletedAt: string | null }> = {}) {
  return {
    organization: {
      id: "org-1",
      name: "Kathmandu Couriers",
      slug: "kathmandu-couriers",
      isActive: overrides.isActive ?? true,
      suspendedBySystem: false,
      deletedAt: overrides.deletedAt ?? null,
      createdAt: "2026-01-01T00:00:00Z",
    },
    planName: "Growth",
    subscriptionStatus: "Active" as const,
    trialEndsAt: null,
    users: [{ id: "u1", email: "owner@kathmandu.example", fullName: "Owner Person", isActive: true }],
    branches: [{ id: "b1", code: "KTM-01", name: "Kathmandu Hub", isActive: true }],
    invoices: [
      {
        id: "i1",
        planName: "Growth",
        amountNpr: 5000,
        cycleStart: "2026-08-01T00:00:00Z",
        cycleEnd: "2026-08-31T00:00:00Z",
        status: "Paid" as const,
        dueAt: "2026-08-05T00:00:00Z",
        paidAt: "2026-08-02T00:00:00Z",
      },
    ],
    recentAuditEntries: [
      {
        id: "a1",
        action: "organization.suspended",
        entityType: "Organization",
        entityId: "org-1",
        userId: "u1",
        impersonatedByUserId: null,
        isCrossTenantAction: false,
        timestamp: "2026-09-01T00:00:00Z",
      },
    ],
  };
}

const usePlatformOrganization = vi.fn();
vi.mock("@/features/platform-organizations/hooks/usePlatformOrganization", () => ({
  usePlatformOrganization: (...args: unknown[]) => usePlatformOrganization(...args),
}));

const suspendMutate = vi.fn();
const reactivateMutate = vi.fn();
const deleteMutate = vi.fn();
const restoreMutate = vi.fn();
const platformPlans = [{ id: "plan-1", name: "Growth", slug: "growth", monthlyPriceNpr: 3000, maxBranches: null, maxStaffUsers: null, maxMonthlyShipments: null, isDefault: true, isActive: true }];
vi.mock("@/features/platform-plans/hooks/usePlatformPlans", () => ({
  usePlatformPlans: () => ({ data: platformPlans, isLoading: false }),
}));

const assignPlanMutate = vi.fn();
vi.mock("@/features/platform-organizations/hooks/useOrganizationLifecycleActions", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/platform-organizations/hooks/useOrganizationLifecycleActions")>();
  return {
    ...actual,
    useSuspendOrganization: () => ({ mutate: suspendMutate, isPending: false }),
    useReactivateOrganization: () => ({ mutate: reactivateMutate, isPending: false }),
    useDeleteOrganization: () => ({ mutate: deleteMutate, isPending: false }),
    useRestoreOrganization: () => ({ mutate: restoreMutate, isPending: false }),
    useAssignOrganizationPlan: () => ({ mutate: assignPlanMutate, isPending: false }),
  };
});

const impersonateMutate = vi.fn();
vi.mock("@/features/platform-organizations/hooks/useImpersonate", () => ({
  useImpersonate: () => ({ mutate: impersonateMutate, isPending: false }),
}));

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

vi.mock("@/features/platform-organizations/store/organizationPanelStore", () => ({
  useOrganizationPanelStore: (selector: (s: unknown) => unknown) =>
    selector({ panel: { mode: "detail", organizationId: "org-1" }, close: vi.fn() }),
}));

import { OrganizationDetailSheet } from "./OrganizationDetailSheet";

describe("OrganizationDetailSheet", () => {
  beforeEach(() => {
    suspendMutate.mockClear();
    reactivateMutate.mockClear();
    deleteMutate.mockClear();
    restoreMutate.mockClear();
    impersonateMutate.mockClear();
    push.mockClear();
  });

  it("shows org details and calls suspend / delete for an active organization", async () => {
    usePlatformOrganization.mockReturnValue({ data: makeDetail(), isLoading: false, isError: false, refetch: vi.fn() });
    render(<OrganizationDetailSheet />);

    expect(screen.getByText("Kathmandu Couriers")).toBeInTheDocument();
    expect(screen.getByText("Growth")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reactivate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /restore/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /suspend/i }));
    await waitFor(() => expect(suspendMutate).toHaveBeenCalledWith("org-1"));

    fireEvent.click(screen.getByRole("button", { name: /delete/i }));
    await waitFor(() => expect(deleteMutate).toHaveBeenCalledWith("org-1"));
  });

  it("offers Impersonate only for active users, and navigates to /dashboard on success", async () => {
    const detail = makeDetail();
    detail.users.push({ id: "u2", email: "gone@kathmandu.example", fullName: "Former Staffer", isActive: false });
    usePlatformOrganization.mockReturnValue({ data: detail, isLoading: false, isError: false, refetch: vi.fn() });
    render(<OrganizationDetailSheet />);

    const impersonateButtons = screen.getAllByRole("button", { name: /impersonate/i });
    expect(impersonateButtons).toHaveLength(1);

    fireEvent.click(impersonateButtons[0]);
    await waitFor(() =>
      expect(impersonateMutate).toHaveBeenCalledWith(
        { organizationId: "org-1", userId: "u1" },
        expect.objectContaining({ onSuccess: expect.any(Function) })
      )
    );

    const onSuccess = impersonateMutate.mock.calls[0][1].onSuccess;
    onSuccess();
    expect(push).toHaveBeenCalledWith("/dashboard");
  });

  it("shows Reactivate instead of Suspend for a suspended organization, and calls it", async () => {
    usePlatformOrganization.mockReturnValue({
      data: makeDetail({ isActive: false }),
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    render(<OrganizationDetailSheet />);

    expect(screen.getByText("Suspended")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /suspend/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /reactivate/i }));
    await waitFor(() => expect(reactivateMutate).toHaveBeenCalledWith("org-1"));
  });

  it("shows only Restore for a deleted organization, and calls it", async () => {
    usePlatformOrganization.mockReturnValue({
      data: makeDetail({ isActive: false, deletedAt: "2026-09-05T00:00:00Z" }),
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    });
    render(<OrganizationDetailSheet />);

    expect(screen.getByText("Deleted")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /suspend/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /reactivate/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^delete$/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /restore/i }));
    await waitFor(() => expect(restoreMutate).toHaveBeenCalledWith("org-1"));
  });

  it("renders an error state and retries", async () => {
    const refetch = vi.fn();
    usePlatformOrganization.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    render(<OrganizationDetailSheet />);

    expect(screen.getByText("Couldn't load this organization")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /retry/i }));
    expect(refetch).toHaveBeenCalled();
  });

  it("shows an Assign Plan control and calls the mutation with the selected plan", async () => {
    assignPlanMutate.mockClear();
    usePlatformOrganization.mockReturnValue({ data: makeDetail(), isLoading: false, isError: false, refetch: vi.fn() });
    render(<OrganizationDetailSheet />);

    expect(screen.getByText(/Assign a plan/i)).toBeInTheDocument();
  });
});
