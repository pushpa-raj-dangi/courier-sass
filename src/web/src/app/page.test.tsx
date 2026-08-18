import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import HomePage from "./page";

describe("HomePage", () => {
  it("renders the platform heading", () => {
    render(<HomePage />);
    expect(
      screen.getByRole("heading", { name: /courier platform/i })
    ).toBeInTheDocument();
  });
});
