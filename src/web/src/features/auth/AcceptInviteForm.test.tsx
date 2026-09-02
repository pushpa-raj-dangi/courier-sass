import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const pushMock = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
  useSearchParams: () => new URLSearchParams("token=abc123"),
}));

import { AcceptInviteForm } from "./AcceptInviteForm";

describe("AcceptInviteForm", () => {
  it("submits the token and password, then redirects to /dashboard on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ ok: true }) });
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();

    render(<AcceptInviteForm />);
    await user.type(screen.getByLabelText("Password"), "N3wP@ssword!");
    await user.type(screen.getByLabelText("Confirm Password"), "N3wP@ssword!");
    await user.click(screen.getByRole("button", { name: /set password/i }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/dashboard"));
    expect(fetchMock).toHaveBeenCalledWith("/api/auth/accept-invite", expect.objectContaining({
      body: JSON.stringify({ token: "abc123", password: "N3wP@ssword!" }),
    }));
  });

  it("shows a validation error when passwords don't match", async () => {
    const user = userEvent.setup();
    render(<AcceptInviteForm />);

    await user.type(screen.getByLabelText("Password"), "N3wP@ssword!");
    await user.type(screen.getByLabelText("Confirm Password"), "Different1!");
    await user.click(screen.getByRole("button", { name: /set password/i }));

    expect(await screen.findByText("Passwords don't match.")).toBeInTheDocument();
  });
});
