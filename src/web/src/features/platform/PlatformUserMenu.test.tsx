import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

import { PlatformUserMenu } from "./PlatformUserMenu";

describe("PlatformUserMenu", () => {
  beforeEach(() => {
    push.mockClear();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true }));
  });

  it("signs out through the platform logout endpoint and returns to /platform-login", async () => {
    const user = userEvent.setup();
    render(<PlatformUserMenu />);

    await user.click(screen.getByRole("button", { name: /platform admin/i }));
    await user.click(await screen.findByRole("menuitem", { name: /sign out/i }));

    expect(fetch).toHaveBeenCalledWith("/api/platform-auth/logout", { method: "POST" });
    expect(push).toHaveBeenCalledWith("/platform-login");
  });
});
