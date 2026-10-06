import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, within } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";

const useOrgDashboard = vi.fn();
vi.mock("@/features/reports/hooks/useOrgDashboard", () => ({ useOrgDashboard: (p: unknown) => useOrgDashboard(p) }));
const shipmentsSummary = vi.fn();
vi.mock("@/features/shipments/hooks/useShipmentsSummary", () => ({ useShipmentsSummary: () => shipmentsSummary() }));
const ndr = vi.fn();
vi.mock("@/features/ndr/hooks/useNdrsSummary", () => ({ useNdrsSummary: (o: unknown) => ndr(o) }));
vi.mock("@/features/ndr/hooks/useReturnShipmentsSummary", () => ({
  useReturnShipmentsSummary: (o: unknown) => rto(o),
}));
const rto = vi.fn((_o?: unknown) => ({ data: { byRtoStage: {}, merchantConfirmed: 0, pendingConfirmation: 4 }, isLoading: false, isError: false }));
const manifestsSummary = vi.fn((_o?: unknown) => ({ data: { InTransit: 2 }, isLoading: false, isError: false }));
vi.mock("@/features/manifests/hooks/useManifestsSummary", () => ({ useManifestsSummary: (o: unknown) => manifestsSummary(o) }));
vi.mock("@/features/branches/hooks/useBranches", () => ({ useBranches: () => ({ data: [{ id: "b1", name: "Kathmandu Hub", code: "KTM" }] }) }));
vi.mock("@/features/shipments/hooks/usePeopleNames", () => ({
  usePeopleNames: () => ({ riderName: () => "Rider", userName: () => "Staff", branchName: () => "Kathmandu Hub" }),
}));
let perms = new Set(["report.read", "ndr.read", "rto.read", "manifest.read", "cod.read", "shipment.create"]);
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: (p: string) => perms.has(p) }));
vi.mock("@/features/shipments/store/shipmentPanelStore", () => ({
  useShipmentPanelStore: (selector: (s: object) => unknown) => selector({ openCreate: vi.fn() }),
}));
vi.mock("@/features/reports/components/ExportReportsCard", () => ({ ExportReportsCard: () => <div>export-card</div> }));

import DashboardPage from "./page";

const summary = {
  todaysShipmentsByStatus: {},
  financial: { expected: 1000, collected: 700, pending: 300, settled: 500, merchantPayable: 450 },
  branchPerformance: {
    b1: { shipmentVolume: 40, deliveryAttempts: 20, deliverySuccesses: 18, deliverySuccessRate: 0.9, ndrCount: 2, ndrRate: 0.1, rtoCount: 1, rtoRate: 0.05 },
  },
  riderPerformance: [{ riderId: "r1", attempts: 10, successes: 9, successRate: 0.9 }],
  overallDeliverySuccessRate: 0.914,
  overallNdrRate: 0.052,
  overallRtoRate: null,
  averageDeliveryTime: "1.19:12:00",
};

describe("DashboardPage", () => {
  beforeEach(() => {
    perms = new Set(["report.read", "ndr.read", "rto.read", "manifest.read", "cod.read", "shipment.create"]);
    useOrgDashboard.mockReset().mockReturnValue({ data: summary, isLoading: false, isError: false, refetch: vi.fn() });
    ndr.mockReturnValue({ data: { Open: 3, Resolved: 9 }, isLoading: false, isError: false });
    shipmentsSummary.mockReturnValue({ data: { Created: 2, InTransit: 7, OutForDelivery: 6 }, isLoading: false, isError: false });
  });

  it("renders the pipeline from current WIP plus delivered in range", () => {
    renderWithIntl(<DashboardPage />);
    const pipeline = screen.getByRole("list", { name: "Parcel pipeline" });
    expect(within(pipeline).getByRole("link", { name: /In transit\s*7/ })).toHaveAttribute("href", "/shipments?tab=in-transit");
    expect(within(pipeline).getByText("18")).toBeInTheDocument();
  });

  it("renders KPI tiles without deltas", () => {
    renderWithIntl(<DashboardPage />);
    expect(screen.getByText("91.4%")).toBeInTheDocument();
    expect(screen.getByText("1.8 days")).toBeInTheDocument();
    expect(document.querySelector("[data-tone]")).toBeNull();
    expect(document.querySelector("[data-rate-bar]")).not.toBeNull();
  });

  it("shows needs-action rows the user may see and a slot for the rest", () => {
    renderWithIntl(<DashboardPage />);
    const card = screen.getByRole("region", { name: "Needs action" });
    expect(within(card).getByRole("link", { name: /3\s*Open NDRs/ })).toHaveAttribute("href", "/ndrs");
    expect(within(card).getByRole("link", { name: /4\s*RTOs awaiting merchant sign-off/ })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Hourly flow" })).toHaveAttribute("data-coming-soon");
  });

  it("hides a needs-action row without its permission", () => {
    perms.delete("ndr.read");
    renderWithIntl(<DashboardPage />);
    expect(screen.queryByText("Open NDRs")).toBeNull();
  });

  it("does not fetch summaries the user may not read", () => {
    perms = new Set(["report.read", "rto.read"]);
    renderWithIntl(<DashboardPage />);
    expect(ndr).toHaveBeenLastCalledWith({ enabled: false });
    expect(manifestsSummary).toHaveBeenLastCalledWith({ enabled: false });
    expect(rto).toHaveBeenLastCalledWith({ enabled: true });
  });

  it("keeps the page when one summary endpoint fails", () => {
    ndr.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    renderWithIntl(<DashboardPage />);
    expect(screen.getByRole("list", { name: "Parcel pipeline" })).toBeInTheDocument();
    expect(screen.getByText("Branch performance")).toBeInTheDocument();
  });

  it("switches range and sends UTC-anchored bounds", () => {
    renderWithIntl(<DashboardPage />);
    fireEvent.mouseDown(screen.getByRole("tab", { name: "7 days" }));
    const last = useOrgDashboard.mock.calls.at(-1)![0] as { from: string; to: string };
    expect(last.from).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00Z$/);
    expect(last.to).toMatch(/^\d{4}-\d{2}-\d{2}T23:59:59Z$/);
  });

  it("shows an error state when the org dashboard fails but keeps the pipeline", () => {
    useOrgDashboard.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderWithIntl(<DashboardPage />);
    expect(screen.getByText("Couldn't load performance data")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Parcel pipeline" })).toBeInTheDocument();
  });

  it("shows a skeleton, not zeros, while the status summary loads", () => {
    shipmentsSummary.mockReturnValue({ data: undefined, isLoading: true, isError: false });
    renderWithIntl(<DashboardPage />);
    expect(screen.queryByRole("list", { name: "Parcel pipeline" })).toBeNull();
    expect(screen.getByRole("status", { name: "Loading parcel pipeline" })).toBeInTheDocument();
  });

  it("shows delivered-in-range as unknown when the org dashboard fails", () => {
    useOrgDashboard.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: vi.fn() });
    renderWithIntl(<DashboardPage />);
    const pipeline = screen.getByRole("list", { name: "Parcel pipeline" });
    const delivered = within(pipeline).getByText("Delivered").closest("li")!;
    expect(delivered).toHaveTextContent("—");
    expect(delivered).not.toHaveTextContent(/[0-9]/);
  });

  it("draws no success-rate bar for a branch without a rate", () => {
    useOrgDashboard.mockReturnValue({
      data: { ...summary, branchPerformance: { b1: { ...summary.branchPerformance.b1, deliverySuccessRate: null } } },
      isLoading: false, isError: false, refetch: vi.fn(),
    });
    renderWithIntl(<DashboardPage />);
    expect(document.querySelector("[data-rate-bar]")).toBeNull();
  });
});
