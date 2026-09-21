import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ManifestReconcileDiagram } from "./ManifestReconcileDiagram";
import { MANIFEST_SAMPLE } from "../content";

describe("ManifestReconcileDiagram", () => {
  it("states expected, scanned and missing counts as text", () => {
    render(<ManifestReconcileDiagram />);
    const figure = screen.getByRole("figure", { name: /manifest/i });
    expect(figure).toHaveTextContent(`Expected ${MANIFEST_SAMPLE.expected}`);
    expect(figure).toHaveTextContent(`Scanned ${MANIFEST_SAMPLE.expected - MANIFEST_SAMPLE.missingIndices.length}`);
    expect(figure).toHaveTextContent(`Missing ${MANIFEST_SAMPLE.missingIndices.length}`);
  });
  it("draws one cell per expected shipment and flags missing ones without relying on color alone", () => {
    const { container } = render(<ManifestReconcileDiagram />);
    expect(container.querySelectorAll("[data-cell]")).toHaveLength(MANIFEST_SAMPLE.expected);
    expect(container.querySelectorAll("[data-cell-missing]")).toHaveLength(MANIFEST_SAMPLE.missingIndices.length);
    expect(container.querySelectorAll("[data-cell-missing-mark]").length).toBeGreaterThan(0);
  });
});
