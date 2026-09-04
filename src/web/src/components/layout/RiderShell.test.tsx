import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RiderShell } from "./RiderShell";

vi.mock("next/navigation", () => ({ usePathname: () => "/rider/tasks" }));
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount: 0, stuckItems: [], lastSyncedAt: null, refresh: vi.fn() }),
}));

describe("RiderShell", () => {
  it("renders the three bottom tabs and the page content", () => {
    render(<RiderShell><p>Page content</p></RiderShell>);
    expect(screen.getByText("Page content")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /tasks/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /scan/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /profile/i })).toBeInTheDocument();
  });
});
