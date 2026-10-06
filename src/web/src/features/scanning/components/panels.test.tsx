import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";

const missing = vi.fn();
vi.mock("@/features/manifests/hooks/useMissingShipments", () => ({ useMissingShipments: () => missing() }));
const closeMutate = vi.fn();
vi.mock("@/features/manifests/hooks/useCloseManifest", () => ({ useCloseManifest: () => ({ mutate: closeMutate, isPending: false }) }));
vi.mock("@/features/auth/hooks/usePermission", () => ({ usePermission: () => true }));

import { ScanResultCard } from "./ScanResultCard";
import { ScanLog } from "./ScanLog";
import { ManualScanInput } from "./ManualScanInput";
import { ReconciliationPanel } from "./ReconciliationPanel";

const entry = (ok: boolean) => ({ id: 1, at: "2026-10-05T05:06:41Z", awb: "QA-388", modeId: "receive" as const, ok, text: ok ? "Received on MNF-1" : "This shipment isn't on this manifest." });

describe("scan panels", () => {
  beforeEach(() => {
    missing.mockReturnValue({ data: Array.from({ length: 7 }, (_, i) => ({ shipmentId: `s${i}`, awb: `QA-${i}`, scannedAt: "" })), isLoading: false, isError: false });
    closeMutate.mockReset();
  });

  it("announces accepted and rejected results", () => {
    const { rerender } = render(<ScanResultCard entry={entry(true)} />);
    expect(screen.getByRole("status")).toHaveTextContent("Accepted");
    expect(screen.getByRole("status")).toHaveTextContent("QA-388");
    rerender(<ScanResultCard entry={entry(false)} />);
    expect(screen.getByRole("status")).toHaveTextContent("Rejected");
    expect(screen.getByRole("status")).toHaveTextContent("isn't on this manifest");
  });

  it("lists the session log with a count", () => {
    render(<ScanLog entries={[entry(true), { ...entry(false), id: 2 }]} />);
    expect(screen.getByText("2 scans this session")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(2);
  });

  it("manual input submits uppercased on Enter, clears on Escape, and respects disabled", () => {
    const onSubmit = vi.fn();
    const { rerender } = render(<ManualScanInput disabled={false} onSubmit={onSubmit} />);
    const input = screen.getByRole("textbox", { name: /scan or type awb/i });
    fireEvent.change(input, { target: { value: " qa-1 " } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onSubmit).toHaveBeenCalledWith("QA-1");
    expect(input).toHaveValue("");
    fireEvent.change(input, { target: { value: "x" } });
    fireEvent.keyDown(input, { key: "Escape" });
    expect(input).toHaveValue("");
    rerender(<ManualScanInput disabled onSubmit={onSubmit} />);
    expect(screen.getByRole("textbox", { name: /scan or type awb/i })).toBeDisabled();
  });

  it("reconciles a manifest: verified/expected, first five pending, show all, close", () => {
    renderWithIntl(<ReconciliationPanel manifest={{ id: "m1", code: "MNF-1", shipmentCount: 10, status: "InTransit" } as never} />);
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("/ 10 verified")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(5);
    fireEvent.click(screen.getByRole("button", { name: "Show all 7" }));
    expect(screen.getAllByRole("listitem")).toHaveLength(7);
    fireEvent.click(screen.getByRole("button", { name: "Close manifest" }));
    fireEvent.click(screen.getByRole("button", { name: "Close and flag 7 missing" }));
    expect(closeMutate).toHaveBeenCalled();
  });
});
