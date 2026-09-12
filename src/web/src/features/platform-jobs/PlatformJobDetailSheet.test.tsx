import { describe, expect, it, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { usePlatformJobPanelStore } from "./store/platformJobPanelStore";
import { PlatformJobDetailSheet } from "./PlatformJobDetailSheet";

const failedJob = {
  jobId: "job-1",
  jobName: "WebhookRetryJob",
  state: "Failed",
  at: "2026-09-01T10:00:00Z",
  exceptionMessage: "Connection refused",
};

describe("PlatformJobDetailSheet", () => {
  beforeEach(() => {
    usePlatformJobPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("renders nothing (closed) until a job is opened", () => {
    render(<PlatformJobDetailSheet />);

    expect(screen.queryByText("WebhookRetryJob")).not.toBeInTheDocument();
  });

  it("shows the job name and exception message once opened", () => {
    usePlatformJobPanelStore.getState().openDetail(failedJob);
    render(<PlatformJobDetailSheet />);

    expect(screen.getByText("WebhookRetryJob")).toBeInTheDocument();
    expect(screen.getByText("Connection refused")).toBeInTheDocument();
  });
});
