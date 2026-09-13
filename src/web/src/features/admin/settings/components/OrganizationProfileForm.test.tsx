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
  },
  security: { refreshTokenLifetimeDays: 7, minPasswordLength: 8 },
  defaults: { defaultServiceTypeId: null, defaultOriginBranchId: null },
};

vi.mock("@/features/admin/settings/hooks/useOrganizationSettings", () => ({
  useOrganizationSettings: () => ({ data: settingsData, isLoading: false }),
}));

const mutateAsync = vi.fn();
vi.mock("@/features/admin/settings/hooks/useUpdateOrganizationProfile", () => ({
  useUpdateOrganizationProfile: () => ({ mutateAsync, isPending: false }),
}));

import { OrganizationProfileForm } from "./OrganizationProfileForm";

describe("OrganizationProfileForm", () => {
  it("loads the API profile values into the corresponding form fields", () => {
    render(<OrganizationProfileForm />);

    expect(screen.getByLabelText("Support Email")).toHaveValue("support@example.test");
    expect(screen.getByLabelText("Support Phone")).toHaveValue("01-4444444");
    expect(screen.getByLabelText("Address")).toHaveValue("New Road, Kathmandu");
    expect(screen.getByLabelText("Logo URL")).toHaveValue("");
    expect(screen.getByLabelText("Timezone")).toHaveValue("Asia/Kathmandu");
    expect(screen.getByLabelText("Business Hours")).toHaveValue("9:00 AM - 6:00 PM");
  });

  it("coerces empty-string fields back to null when submitting", async () => {
    render(<OrganizationProfileForm />);

    // logoUrl arrives from the API as null, reset() maps it to "" in the form — submit must
    // coerce it back to null, not send an empty string to the backend.
    expect(screen.getByLabelText("Logo URL")).toHaveValue("");

    fireEvent.click(screen.getByRole("button", { name: /save profile/i }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith({
      supportEmail: "support@example.test",
      supportPhone: "01-4444444",
      addressLine: "New Road, Kathmandu",
      logoUrl: null,
      timeZoneId: "Asia/Kathmandu",
      businessHours: "9:00 AM - 6:00 PM",
    });
  });
});
