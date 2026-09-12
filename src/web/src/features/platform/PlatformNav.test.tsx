import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const push = vi.fn();

vi.mock("next/navigation", () => ({
  usePathname: () => "/platform/plans",
  useRouter: () => ({ push }),
}));

import { PlatformNav } from "./PlatformNav";

describe("PlatformNav", () => {
  beforeEach(() => {
    push.mockClear();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true }));
  });

  it("renders all seven platform nav links", () => {
    render(<PlatformNav />);

    expect(screen.getByText("Dashboard")).toBeInTheDocument();
    expect(screen.getByText("Organizations")).toBeInTheDocument();
    expect(screen.getByText("Plans")).toBeInTheDocument();
    expect(screen.getByText("Invoices")).toBeInTheDocument();
    expect(screen.getByText("Audit Logs")).toBeInTheDocument();
    expect(screen.getByText("Jobs")).toBeInTheDocument();
    expect(screen.getByText("Queues")).toBeInTheDocument();
  });

  it("highlights the active page based on the current pathname", () => {
    render(<PlatformNav />);

    expect(screen.getByText("Plans")).toHaveClass("text-foreground");
    expect(screen.getByText("Dashboard")).not.toHaveClass("text-foreground");
  });

  it("renders a sign-out control that logs out and navigates to /platform-login", async () => {
    const user = userEvent.setup();
    render(<PlatformNav />);

    const signOutButton = screen.getByRole("button", { name: /sign out/i });
    expect(signOutButton).toBeInTheDocument();

    await user.click(signOutButton);

    expect(fetch).toHaveBeenCalledWith("/api/platform-auth/logout", { method: "POST" });
    expect(push).toHaveBeenCalledWith("/platform-login");
  });
});
