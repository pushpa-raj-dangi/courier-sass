import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import type { OrganizationSettings } from "@/features/admin/settings/types";

const settingsData: OrganizationSettings = {
  profile: {
    supportEmail: "support@example.test",
    supportPhone: "01-4444444",
    addressLine: "New Road, Kathmandu",
    logoUrl: null,
    timeZoneId: "Asia/Kathmandu",
    businessHours: "9:00 AM - 6:00 PM",
    defaultLocale: "en",
    calendarPreference: "Gregorian",
  },
  security: { refreshTokenLifetimeDays: 7, minPasswordLength: 8 },
  defaults: { defaultServiceTypeId: null, defaultOriginBranchId: null },
};

vi.mock("@/features/admin/settings/hooks/useOrganizationSettings", () => ({
  useOrganizationSettings: () => ({ data: settingsData, isLoading: false }),
}));

const updateSecurityMutateAsync = vi.fn();
vi.mock("@/features/admin/settings/hooks/useUpdateSecurityPolicy", () => ({
  useUpdateSecurityPolicy: () => ({ mutateAsync: updateSecurityMutateAsync, isPending: false }),
}));

const revokeAllSessionsMutateAsync = vi.fn();
vi.mock("@/features/admin/settings/hooks/useRevokeAllSessions", () => ({
  useRevokeAllSessions: () => ({ mutateAsync: revokeAllSessionsMutateAsync, isPending: false }),
}));

import { SecurityPolicyForm } from "./SecurityPolicyForm";

describe("SecurityPolicyForm", () => {
  it("loads the API security values into the corresponding form fields", () => {
    render(<SecurityPolicyForm />);

    expect(screen.getByLabelText("Session Length (days)")).toHaveValue(7);
    expect(screen.getByLabelText("Minimum Password Length")).toHaveValue(8);
  });

  it("submits the entered values as the update payload", async () => {
    render(<SecurityPolicyForm />);

    fireEvent.change(screen.getByLabelText("Session Length (days)"), { target: { value: "14" } });
    fireEvent.change(screen.getByLabelText("Minimum Password Length"), { target: { value: "12" } });
    fireEvent.click(screen.getByRole("button", { name: /save security policy/i }));

    await waitFor(() => expect(updateSecurityMutateAsync).toHaveBeenCalledTimes(1));
    expect(updateSecurityMutateAsync).toHaveBeenCalledWith({
      refreshTokenLifetimeDays: 14,
      minPasswordLength: 12,
    });
  });

  it("calls revokeAllSessions.mutateAsync when Revoke All Sessions is clicked", async () => {
    render(<SecurityPolicyForm />);

    fireEvent.click(screen.getByRole("button", { name: /revoke all sessions/i }));

    await waitFor(() => expect(revokeAllSessionsMutateAsync).toHaveBeenCalledTimes(1));
    expect(await screen.findByText("All sessions revoked.")).toBeInTheDocument();
  });

  it("shows an error message when revoking sessions fails", async () => {
    revokeAllSessionsMutateAsync.mockRejectedValueOnce(new Error("network error"));

    render(<SecurityPolicyForm />);

    fireEvent.click(screen.getByRole("button", { name: /revoke all sessions/i }));

    expect(await screen.findByText("Couldn't revoke sessions. Try again.")).toBeInTheDocument();
  });
});
