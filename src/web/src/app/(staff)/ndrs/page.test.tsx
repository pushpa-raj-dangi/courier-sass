import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { formatAge } from "@/lib/dates/age";
import type { Ndr } from "@/features/ndr/types";

const replace = vi.fn();
let search = "";
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  usePathname: () => "/ndrs",
  useSearchParams: () => new URLSearchParams(search),
}));

const useNdrs = vi.fn();
vi.mock("@/features/ndr/hooks/useNdrs", () => ({ useNdrs: (p: unknown) => useNdrs(p) }));
let summary: { data?: Record<string, number>; isLoading: boolean; isError: boolean; refetch: () => void };
vi.mock("@/features/ndr/hooks/useNdrsSummary", () => ({ useNdrsSummary: () => summary }));
vi.mock("@/features/auth/hooks/useSession", () => ({ useSession: () => ({ data: { userId: "u1" } }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ userName: (id: string | null) => (id === "u2" ? "Sita Rai" : "Staff"), riderName: () => "Rider", branchName: () => "Branch" }),
}));
const openDetail = vi.fn();
vi.mock("@/features/ndr/store/ndrPanelStore", () => ({
  useNdrPanelStore: (selector: (s: unknown) => unknown) => selector({ panel: { mode: "closed" }, openDetail }),
}));

import NdrsPage from "./page";

const hoursAgo = (h: number) => new Date(Date.now() - h * 3_600_000).toISOString();

function ndr(overrides: Partial<Ndr>): Ndr {
  return {
    id: "n1", shipmentId: "s1", awb: "KTM-000301", reason: "WrongAddress", assignedStaffUserId: null, status: "Open",
    customerResponse: null, followUpAt: null, nextAction: null, resolution: null, resolvedAt: null, createdAt: hoursAgo(2),
    ...overrides,
  };
}

function page(items: Ndr[]) {
  return { data: { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }, isLoading: false, isError: false, refetch: vi.fn() };
}

beforeEach(() => {
  replace.mockReset();
  openDetail.mockReset();
  search = "";
  summary = { data: { Open: 4, Contacting: 2, Resolved: 9 }, isLoading: false, isError: false, refetch: vi.fn() };
  useNdrs.mockReset();
  useNdrs.mockReturnValue(page([ndr({})]));
});

describe("NdrsPage", () => {
  it("shows the six-stage strip with Open highlighted when NDRs are open", () => {
    render(<NdrsPage />);
    expect(screen.getByRole("heading", { name: "Failed deliveries" })).toBeInTheDocument();
    expect(screen.getByRole("tablist", { name: "NDR and returns" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "NDR status" }).querySelectorAll("li")).toHaveLength(6);
    expect(screen.getByRole("button", { name: /^Open/ })).toHaveAttribute("aria-current", "step");
  });

  it("highlights nothing when no NDR is open, and never shows zeros while loading", () => {
    summary = { data: { Open: 0, Resolved: 3 }, isLoading: false, isError: false, refetch: vi.fn() };
    const { unmount } = render(<NdrsPage />);
    expect(screen.getByRole("button", { name: /^Open/ })).not.toHaveAttribute("aria-current");
    unmount();
    summary = { data: undefined, isLoading: true, isError: false, refetch: vi.fn() };
    render(<NdrsPage />);
    expect(screen.getByRole("status", { name: "Loading NDR counts" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "NDR status" })).toBeNull();
  });

  it("passes URL filters to the list hook and resets to page 1", () => {
    search = "status=Contacting&assigned=me&reason=WrongAddress";
    render(<NdrsPage />);
    expect(useNdrs).toHaveBeenCalledWith({ status: "Contacting", assigned: "me", reason: "WrongAddress", page: 1, pageSize: 20 });
  });

  it("toggles the mutually exclusive owner chips in the URL", () => {
    render(<NdrsPage />);
    fireEvent.click(screen.getByRole("button", { name: "Assigned to me" }));
    expect(replace).toHaveBeenLastCalledWith("/ndrs?assigned=me", { scroll: false });

    search = "assigned=me";
    render(<NdrsPage />);
    const mine = screen.getAllByRole("button", { name: "Assigned to me" })[1];
    expect(mine).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(screen.getAllByRole("button", { name: "Unassigned" })[1]);
    expect(replace).toHaveBeenLastCalledWith("/ndrs?assigned=unassigned", { scroll: false });
    fireEvent.click(mine);
    expect(replace).toHaveBeenLastCalledWith("/ndrs", { scroll: false });
  });

  it("sets and clears the status filter from the strip", () => {
    render(<NdrsPage />);
    fireEvent.click(screen.getByRole("button", { name: /^Contacting/ }));
    expect(replace).toHaveBeenLastCalledWith("/ndrs?status=Contacting", { scroll: false });
  });

  it("warns on NDRs open 24 h or more, with the age as text", () => {
    const old = hoursAgo(30);
    useNdrs.mockReturnValue(page([ndr({ id: "n1", createdAt: old }), ndr({ id: "n2", awb: "KTM-000302", createdAt: hoursAgo(2) })]));
    render(<NdrsPage />);
    expect(screen.getByText(formatAge(old))).toHaveClass("text-destructive");
    expect(screen.getByText(formatAge(hoursAgo(2)))).not.toHaveClass("text-destructive");
  });

  it("labels owners, reasons and overdue follow-ups", () => {
    useNdrs.mockReturnValue(page([
      ndr({ id: "n1", assignedStaffUserId: "u1", status: "Contacting", followUpAt: hoursAgo(1) }),
      ndr({ id: "n2", awb: "KTM-000302", assignedStaffUserId: "u2", reason: "CustomerRequestedReschedule" }),
      ndr({ id: "n3", awb: "KTM-000303" }),
    ]));
    render(<NdrsPage />);
    const table = screen.getByRole("table");
    expect(within(table).getByText("You")).toBeInTheDocument();
    expect(within(table).getByText("Sita Rai")).toBeInTheDocument();
    expect(within(table).getByText("Unassigned")).toBeInTheDocument();
    expect(within(table).getByText("Customer asked to reschedule")).toBeInTheDocument();
    expect(within(table).getByText(/Follow-up overdue/)).toBeInTheDocument();
    fireEvent.click(within(table).getByRole("button", { name: "KTM-000302" }));
    expect(openDetail).toHaveBeenCalledWith("n2");
  });

  it("shows an error state with retry when the queue fails", () => {
    useNdrs.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    render(<NdrsPage />);
    expect(screen.getByText("Couldn't load NDRs")).toBeInTheDocument();
  });
});
