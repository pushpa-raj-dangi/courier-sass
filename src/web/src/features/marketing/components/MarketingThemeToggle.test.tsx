import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const { useThemeMock } = vi.hoisted(() => ({ useThemeMock: vi.fn() }));

vi.mock("next-themes", () => ({ useTheme: useThemeMock }));

const { MarketingThemeToggle } = await import("./MarketingThemeToggle");

describe("MarketingThemeToggle", () => {
  it("offers to switch to dark mode when currently light", () => {
    const setTheme = vi.fn();
    useThemeMock.mockReturnValue({ resolvedTheme: "light", setTheme });
    render(<MarketingThemeToggle />);
    expect(screen.getByRole("button", { name: "Switch to dark mode" })).toBeInTheDocument();
  });

  it("offers to switch to light mode when currently dark", () => {
    const setTheme = vi.fn();
    useThemeMock.mockReturnValue({ resolvedTheme: "dark", setTheme });
    render(<MarketingThemeToggle />);
    expect(screen.getByRole("button", { name: "Switch to light mode" })).toBeInTheDocument();
  });

  it("calls setTheme with the opposite theme on click", async () => {
    const setTheme = vi.fn();
    useThemeMock.mockReturnValue({ resolvedTheme: "light", setTheme });
    render(<MarketingThemeToggle />);
    await userEvent.click(screen.getByRole("button", { name: "Switch to dark mode" }));
    expect(setTheme).toHaveBeenCalledWith("dark");
  });
});
