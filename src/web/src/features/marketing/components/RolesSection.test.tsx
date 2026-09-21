import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RolesSection } from "./RolesSection";
import { CREDIBILITY_POINTS, PERMISSION_SCOPE_EXAMPLE } from "../content";

describe("RolesSection", () => {
  it("shows the permission + branch-scope example and the credibility points", () => {
    const { container } = render(<RolesSection />);
    expect(container.querySelector("section#roles")).not.toBeNull();
    expect(screen.getByText(PERMISSION_SCOPE_EXAMPLE.branch)).toBeInTheDocument();
    for (const permission of PERMISSION_SCOPE_EXAMPLE.permissions) {
      expect(screen.getByText(permission)).toBeInTheDocument();
    }
    for (const point of CREDIBILITY_POINTS) {
      expect(screen.getByText(point.title)).toBeInTheDocument();
    }
  });
});
