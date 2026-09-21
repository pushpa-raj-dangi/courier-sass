import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ScanSection } from "./ScanSection";
import { SCAN_CHECKS, SCAN_OPERATIONS } from "../content";

describe("ScanSection", () => {
  it("lists every scan operation and validation check, and shows the manifest reconcile figure", () => {
    const { container } = render(<ScanSection />);
    expect(container.querySelector("section#scan-manifest")).not.toBeNull();
    for (const text of [...SCAN_OPERATIONS, ...SCAN_CHECKS]) {
      expect(screen.getByText(text)).toBeInTheDocument();
    }
    expect(screen.getByRole("figure", { name: /manifest/i })).toBeInTheDocument();
  });
});
