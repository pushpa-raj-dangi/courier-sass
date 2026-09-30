import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithIntl } from "@/test/renderWithIntl";
import { NotificationsButton } from "./NotificationsButton";

describe("NotificationsButton", () => {
  it("opens a popover holding the notifications coming-soon slot", async () => {
    const user = userEvent.setup();
    renderWithIntl(<NotificationsButton />);

    await user.click(screen.getByRole("button", { name: "Notifications" }));

    expect(await screen.findByRole("region", { name: "Notifications" })).toBeInTheDocument();
    expect(screen.getByText("Needs backend")).toBeInTheDocument();
  });
});
