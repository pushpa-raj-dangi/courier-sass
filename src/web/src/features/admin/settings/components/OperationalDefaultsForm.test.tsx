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
  defaults: { defaultServiceTypeId: "st1", defaultOriginBranchId: "b1" },
};

vi.mock("@/features/admin/settings/hooks/useOrganizationSettings", () => ({
  useOrganizationSettings: () => ({ data: settingsData, isLoading: false }),
}));

vi.mock("@/features/branches/hooks/useBranches", () => ({
  useBranches: () => ({
    data: [
      { id: "b1", name: "Kathmandu Hub" },
      { id: "b2", name: "Pokhara Hub" },
    ],
  }),
}));

vi.mock("@/features/shipments/hooks/useServiceTypes", () => ({
  useServiceTypes: () => ({
    data: [
      { id: "st1", code: "STANDARD", name: "Standard" },
      { id: "st2", code: "EXPRESS", name: "Express" },
    ],
  }),
}));

const updateDefaultsMutateAsync = vi.fn();
vi.mock("@/features/admin/settings/hooks/useUpdateOperationalDefaults", () => ({
  useUpdateOperationalDefaults: () => ({ mutateAsync: updateDefaultsMutateAsync, isPending: false }),
}));

import { OperationalDefaultsForm } from "./OperationalDefaultsForm";

describe("OperationalDefaultsForm", () => {
  it("loads the API default values into the corresponding selects", () => {
    render(<OperationalDefaultsForm />);

    expect(screen.getByLabelText("Default service type")).toHaveTextContent("Standard");
    expect(screen.getByLabelText("Default origin branch")).toHaveTextContent("Kathmandu Hub");
  });

  it("submits the selected values as the update payload", async () => {
    render(<OperationalDefaultsForm />);

    fireEvent.click(screen.getByLabelText("Default service type"));
    fireEvent.click(screen.getByRole("option", { name: "Express" }));
    fireEvent.click(screen.getByLabelText("Default origin branch"));
    fireEvent.click(screen.getByRole("option", { name: "Pokhara Hub" }));
    fireEvent.click(screen.getByRole("button", { name: /save defaults/i }));

    await waitFor(() => expect(updateDefaultsMutateAsync).toHaveBeenCalledTimes(1));
    expect(updateDefaultsMutateAsync).toHaveBeenCalledWith({
      defaultServiceTypeId: "st2",
      defaultOriginBranchId: "b2",
    });
  });

  it("submits null when None is selected for both defaults", async () => {
    render(<OperationalDefaultsForm />);

    fireEvent.click(screen.getByLabelText("Default service type"));
    fireEvent.click(screen.getByRole("option", { name: "None" }));
    fireEvent.click(screen.getByLabelText("Default origin branch"));
    fireEvent.click(screen.getByRole("option", { name: "None" }));
    fireEvent.click(screen.getByRole("button", { name: /save defaults/i }));

    await waitFor(() => expect(updateDefaultsMutateAsync).toHaveBeenCalledTimes(1));
    expect(updateDefaultsMutateAsync).toHaveBeenCalledWith({
      defaultServiceTypeId: null,
      defaultOriginBranchId: null,
    });
  });

  it("shows a server error message when the update fails", async () => {
    updateDefaultsMutateAsync.mockRejectedValueOnce(new Error("network error"));

    render(<OperationalDefaultsForm />);
    fireEvent.click(screen.getByRole("button", { name: /save defaults/i }));

    expect(await screen.findByText("Something went wrong. Check the fields and try again.")).toBeInTheDocument();
  });
});
