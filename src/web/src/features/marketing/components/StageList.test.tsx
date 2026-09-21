import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { StageList } from "./StageList";

describe("StageList", () => {
  it("renders a labelled ordered list of stages", () => {
    render(<StageList label="NDR workflow" stages={["Open", "Contacting", "Resolved"]} />);
    expect(screen.getByRole("list", { name: "NDR workflow" })).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(3);
  });
});
