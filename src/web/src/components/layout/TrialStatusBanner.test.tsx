import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

const usePermission = vi.fn();
vi.mock("@/features/auth/hooks/usePermission", () => ({
  usePermission: (...args: unknown[]) => usePermission(...args),
}));

const useTrialStatus = vi.fn();
vi.mock("@/features/billing/hooks/useTrialStatus", () => ({
  useTrialStatus: () => useTrialStatus(),
}));

import { TrialStatusBanner } from "./TrialStatusBanner";

describe("TrialStatusBanner", () => {
  it("renders nothing when there is no subscription data yet", () => {
    usePermission.mockReturnValue(true);
    useTrialStatus.mockReturnValue({ data: undefined });

    const { container } = render(<TrialStatusBanner />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when the subscription is Active", () => {
    usePermission.mockReturnValue(true);
    useTrialStatus.mockReturnValue({ data: { status: "Active", trialEndsAt: null } });

    const { container } = render(<TrialStatusBanner />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows a trial countdown with a Choose a plan link for an OrganizationOwner", () => {
    usePermission.mockReturnValue(true);
    const trialEndsAt = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString();
    useTrialStatus.mockReturnValue({ data: { status: "Trial", trialEndsAt } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/3 days left/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /choose a plan/i })).toHaveAttribute("href", "/billing");
  });

  it("shows a trial countdown without a link for non-owner staff", () => {
    usePermission.mockReturnValue(false);
    const trialEndsAt = new Date(Date.now() + 3 * 24 * 60 * 60 * 1000).toISOString();
    useTrialStatus.mockReturnValue({ data: { status: "Trial", trialEndsAt } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/3 days left/)).toBeInTheDocument();
    expect(screen.getByText(/ask your organization owner/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /choose a plan/i })).not.toBeInTheDocument();
  });

  it("shows the expired message with a Choose a plan link for an OrganizationOwner", () => {
    usePermission.mockReturnValue(true);
    useTrialStatus.mockReturnValue({ data: { status: "TrialExpired", trialEndsAt: null } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/your trial has ended/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /choose a plan/i })).toHaveAttribute("href", "/billing");
  });

  it("shows the expired message without a link for non-owner staff", () => {
    usePermission.mockReturnValue(false);
    useTrialStatus.mockReturnValue({ data: { status: "TrialExpired", trialEndsAt: null } });

    render(<TrialStatusBanner />);

    expect(screen.getByText(/your trial has ended/i)).toBeInTheDocument();
    expect(screen.getByText(/ask your organization owner/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /choose a plan/i })).not.toBeInTheDocument();
  });
});
