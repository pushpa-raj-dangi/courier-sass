import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const settings = [
  {
    key: "Esewa.ProductCode",
    category: "PaymentGateway",
    valueType: "String",
    isSecret: false,
    requiresConfirmation: false,
    description: "eSewa merchant product code.",
    isOverridden: false,
    displayValue: null,
    updatedAt: null,
    updatedByUserId: null,
  },
  {
    key: "Jwt.SigningKey",
    category: "Security",
    valueType: "String",
    isSecret: true,
    requiresConfirmation: true,
    description: "JWT signing key. Rotating this logs out every user platform-wide.",
    isOverridden: true,
    displayValue: "••1234",
    updatedAt: "2026-09-18T00:00:00Z",
    updatedByUserId: "11111111-1111-1111-1111-111111111111",
  },
];

const usePlatformSettings = vi.fn(() => ({ data: settings, isLoading: false, isError: false, refetch: vi.fn() }));
vi.mock("@/features/platform-settings/hooks/usePlatformSettings", () => ({
  usePlatformSettings: () => usePlatformSettings(),
}));
vi.mock("@/features/platform-settings/hooks/useUpdatePlatformSetting", () => ({
  useUpdatePlatformSetting: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@/features/platform-settings/hooks/useResetPlatformSetting", () => ({
  useResetPlatformSetting: () => ({ mutate: vi.fn(), isPending: false }),
}));

import PlatformSettingsPage from "./page";

describe("PlatformSettingsPage", () => {
  it("renders the Payment Gateways tab's settings by default", () => {
    render(<PlatformSettingsPage />);

    expect(screen.getByText("Esewa.ProductCode")).toBeInTheDocument();
  });

  it("shows a Reset to default action for an overridden setting", async () => {
    const user = userEvent.setup();
    render(<PlatformSettingsPage />);

    await user.click(screen.getByRole("tab", { name: "Security" }));

    expect(screen.getAllByText("Reset to default").length).toBeGreaterThan(0);
  });
});
