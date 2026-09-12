import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, within } from "@testing-library/react";

const overview = {
  summary: { enqueued: 1, scheduled: 2, processing: 3, succeeded: 50, failed: 4, deleted: 0 },
  recurringJobs: [
    { id: "outbox-publisher", cron: "*/10 * * * * *", nextExecution: "2026-09-12T10:00:10Z", lastExecution: "2026-09-12T10:00:00Z", lastJobState: "Succeeded" },
  ],
  recentJobs: [
    { jobId: "job-1", jobName: "WebhookRetryJob", state: "Failed", at: "2026-09-12T09:59:00Z", exceptionMessage: "Connection refused" },
  ],
};

const usePlatformJobs = vi.fn(() => ({ data: overview, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-jobs/hooks/usePlatformJobs", () => ({
  usePlatformJobs: () => usePlatformJobs(),
}));

import PlatformJobsPage from "./page";

describe("PlatformJobsPage", () => {
  it("renders the summary counts, the recurring job, and the recent job", () => {
    render(<PlatformJobsPage />);

    expect(screen.getByText("50")).toBeInTheDocument();
    expect(screen.getByText("outbox-publisher")).toBeInTheDocument();
    expect(screen.getByText("WebhookRetryJob")).toBeInTheDocument();
  });

  it("opens the detail Sheet with the exception message when a failed job is clicked", () => {
    render(<PlatformJobsPage />);

    fireEvent.click(screen.getByText("WebhookRetryJob"));

    expect(within(screen.getByRole("dialog")).getByText("Connection refused")).toBeInTheDocument();
  });
});
