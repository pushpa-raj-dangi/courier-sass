import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithIntl } from "@/test/renderWithIntl";
import { readCookie } from "@/i18n/preferences";
import { LanguageSwitcher } from "./LanguageSwitcher";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh }),
}));

describe("LanguageSwitcher", () => {
  beforeEach(() => {
    refresh.mockClear();
    document.cookie = "courier_locale=; Path=/; Max-Age=0";
  });

  it("offers both languages by their native names and marks the current one", async () => {
    renderWithIntl(<LanguageSwitcher />);
    await userEvent.click(screen.getByRole("button", { name: "Language" }));

    expect(await screen.findByRole("menuitemradio", { name: /English/ })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("menuitemradio", { name: /नेपाली/ })).toHaveAttribute("aria-checked", "false");
  });

  it("does nothing when the already-current language is chosen", async () => {
    renderWithIntl(<LanguageSwitcher />);
    await userEvent.click(screen.getByRole("button", { name: "Language" }));
    await userEvent.click(await screen.findByRole("menuitemradio", { name: /English/ }));

    expect(refresh).not.toHaveBeenCalled();
    expect(readCookie("courier_locale")).toBeNull();
  });

  it("stores the chosen locale in the cookie and refreshes the route", async () => {
    renderWithIntl(<LanguageSwitcher />);
    await userEvent.click(screen.getByRole("button", { name: "Language" }));
    await userEvent.click(await screen.findByRole("menuitemradio", { name: /नेपाली/ }));

    expect(readCookie("courier_locale")).toBe("ne");
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("labels the trigger in Nepali when the locale is ne", () => {
    renderWithIntl(<LanguageSwitcher />, { locale: "ne" });
    expect(screen.getByRole("button", { name: "भाषा" })).toBeInTheDocument();
  });
});
