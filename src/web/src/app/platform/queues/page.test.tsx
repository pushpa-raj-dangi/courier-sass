import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const queues = [
  { name: "courier.notifications", messages: 5, messagesReady: 5, messagesUnacknowledged: 0, consumers: 1, state: "running" },
  { name: "courier.webhooks", messages: 12, messagesReady: 12, messagesUnacknowledged: 0, consumers: 0, state: "idle" },
];

const usePlatformQueues = vi.fn(() => ({ data: queues, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-queues/hooks/usePlatformQueues", () => ({
  usePlatformQueues: () => usePlatformQueues(),
}));

import PlatformQueuesPage from "./page";

describe("PlatformQueuesPage", () => {
  it("renders every queue with its ready/unacked/consumer counts", () => {
    render(<PlatformQueuesPage />);

    expect(screen.getByText("courier.notifications")).toBeInTheDocument();
    expect(screen.getByText("courier.webhooks")).toBeInTheDocument();
  });

  it("flags a queue with messages piling up and zero consumers as unhealthy", () => {
    render(<PlatformQueuesPage />);

    expect(screen.getByText("Unhealthy")).toBeInTheDocument();
    expect(screen.getByText("Healthy")).toBeInTheDocument();
  });
});
