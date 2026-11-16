import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { RiderShell } from "./RiderShell";

vi.mock("next/navigation", () => ({ usePathname: () => "/rider/tasks" }));
vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount: 0, pendingItems: [], stuckItems: [], lastSyncedAt: null, refresh: vi.fn() }),
}));

describe("RiderShell", () => {
  it("renders the Run / Scan / Profile tabs and marks the current one", () => {
    render(<RiderShell><p>Page content</p></RiderShell>);
    expect(screen.getByText("Page content")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Run" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Scan" })).not.toHaveAttribute("aria-current");
    expect(screen.getByRole("link", { name: "Profile" })).toHaveAttribute("href", "/rider/profile");
  });
});
