import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const clientFetchMock = vi.fn();
vi.mock("@/lib/api/clientFetch", () => ({ clientFetch: (...a: unknown[]) => clientFetchMock(...a) }));

let canManage = true;
vi.mock("@/features/merchant-auth/hooks/useMerchantPermission", () => ({
  useMerchantPermission: (p: string) => (p === "merchant.staff.manage" ? canManage : true),
}));
vi.mock("@/features/merchant-auth/hooks/useMerchantSession", () => ({
  useMerchantSession: () => ({ data: { merchantDisplayName: "Everest Organics" } }),
}));

import { useMerchantStaffPanelStore } from "@/features/merchant-staff/store/merchantStaffPanelStore";
import { MerchantStaffPage } from "./MerchantStaffPage";
import type { MerchantStaffMember } from "../types";

const staff: MerchantStaffMember[] = [
  { id: "1", fullName: "Sita Sharma", email: "sita@e.com", role: "Owner", isActive: true },
  { id: "2", fullName: "Ram Karki", email: "ram@e.com", role: "Staff", isActive: true },
  { id: "3", fullName: "Prakash Magar", email: "prakash@e.com", role: "Staff", isActive: false },
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MerchantStaffPage />
    </QueryClientProvider>
  );
}

describe("MerchantStaffPage", () => {
  beforeEach(() => {
    clientFetchMock.mockReset();
    canManage = true;
    useMerchantStaffPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("shows the no-access state and never fetches without merchant.staff.manage", () => {
    canManage = false;
    renderPage();
    expect(screen.getByText("You don't have access to this page")).toBeInTheDocument();
    expect(clientFetchMock).not.toHaveBeenCalled();
  });

  it("renders subtitle, role cards with counts, and the staff table", async () => {
    clientFetchMock.mockResolvedValue(staff);
    renderPage();

    expect(await screen.findByText("Sita Sharma")).toBeInTheDocument();
    expect(
      screen.getByText("People who can book shipments and track parcels for Everest Organics")
    ).toBeInTheDocument();

    const roles = screen.getByRole("region", { name: "What each role can do" });
    const ownerCard = within(roles).getByTestId("role-card-Owner");
    expect(within(ownerCard).getByText("1")).toBeInTheDocument();
    expect(within(ownerCard).getByText("Everything, plus staff and API keys")).toBeInTheDocument();
    const staffCard = within(roles).getByTestId("role-card-Staff");
    expect(within(staffCard).getByText("2")).toBeInTheDocument();
    expect(within(staffCard).getByText("Book shipments, request pickups, track parcels")).toBeInTheDocument();

    const rows = screen.getAllByTestId("staff-row");
    expect(rows).toHaveLength(3);
    expect(within(rows[0]).getByText("SS")).toBeInTheDocument();
    expect(within(rows[0]).getByText("sita@e.com")).toBeInTheDocument();
    expect(within(rows[0]).getByText("Active")).toBeInTheDocument();
    expect(within(rows[2]).getByText("Inactive")).toBeInTheDocument();
    expect(rows[2].className).toContain("text-muted-foreground");
    expect(rows[0].className).not.toContain("text-muted-foreground");
    expect(within(screen.getByRole("table")).queryAllByRole("button")).toHaveLength(0);
  });

  it("opens the invite Sheet from Invite staff", async () => {
    clientFetchMock.mockResolvedValue(staff);
    renderPage();
    await screen.findByText("Sita Sharma");
    fireEvent.click(screen.getByRole("button", { name: "Invite staff" }));
    expect(useMerchantStaffPanelStore.getState().panel.mode).toBe("create");
  });

  it("shows an error state on fetch failure", async () => {
    clientFetchMock.mockRejectedValue(new Error("x"));
    renderPage();
    expect(await screen.findByText("Couldn't load staff")).toBeInTheDocument();
  });
});
