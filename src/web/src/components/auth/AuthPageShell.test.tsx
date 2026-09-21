import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { Package } from "lucide-react";
import { AuthPageShell } from "./AuthPageShell";

const base = {
  icon: Package,
  brand: "Courier Platform",
  headline: "Every parcel, accounted for.",
  description: "One system that always knows where a parcel is.",
  switchPrompt: "New here?",
  switchLabel: "Create an organization",
  switchHref: "/register",
};

describe("AuthPageShell", () => {
  it("renders headline, description and the switch link", () => {
    render(
      <AuthPageShell {...base}>
        <div>form</div>
      </AuthPageShell>,
    );

    expect(screen.getByText("Every parcel, accounted for.")).toBeInTheDocument();
    expect(screen.getByText("One system that always knows where a parcel is.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create an organization" })).toHaveAttribute("href", "/register");
    expect(screen.getByText("form")).toBeInTheDocument();
  });

  it("renders topAction before the switch prompt when provided", () => {
    render(
      <AuthPageShell {...base} topAction={<button type="button">Language</button>}>
        <div>form</div>
      </AuthPageShell>,
    );

    const action = screen.getByRole("button", { name: "Language" });
    const link = screen.getByRole("link", { name: "Create an organization" });
    expect(action).toBeInTheDocument();
    expect(action.compareDocumentPosition(link) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("renders no extra control when topAction is omitted", () => {
    render(
      <AuthPageShell {...base}>
        <div>form</div>
      </AuthPageShell>,
    );

    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });
});
