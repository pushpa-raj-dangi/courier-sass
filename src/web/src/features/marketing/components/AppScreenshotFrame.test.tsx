import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { AppScreenshotFrame } from "./AppScreenshotFrame";

describe("AppScreenshotFrame", () => {
  it("renders the image with the given src and alt text", () => {
    render(<AppScreenshotFrame src="/marketing/screenshots/dashboard.webp" alt="Org dashboard preview" />);
    const img = screen.getByRole("img", { name: "Org dashboard preview" });
    expect(img).toHaveAttribute("src", "/marketing/screenshots/dashboard.webp");
  });

  it("renders the decorative browser-chrome header as hidden from assistive tech", () => {
    render(<AppScreenshotFrame src="/marketing/screenshots/dashboard.webp" alt="Org dashboard preview" />);
    const frame = screen.getByTestId("app-screenshot-frame");
    const chromeHeader = frame.querySelector('[aria-hidden="true"]');
    expect(chromeHeader).not.toBeNull();
  });
});
