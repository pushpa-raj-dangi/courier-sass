import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { OutboxStatusBar } from "./OutboxStatusBar";

vi.mock("@/features/rider-app/sync/useOutboxStatus", () => ({
  useOutboxStatus: () => ({ pendingCount: 2, stuckItems: [], lastSyncedAt: null, refresh: vi.fn() }),
}));

describe("OutboxStatusBar", () => {
  it("shows the pending queue count", () => {
    render(<OutboxStatusBar />);
    expect(screen.getByText(/2 pending/i)).toBeInTheDocument();
  });
});
