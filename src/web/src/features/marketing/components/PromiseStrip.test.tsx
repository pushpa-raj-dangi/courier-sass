import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { PromiseStrip } from "./PromiseStrip";
import { PROMISE_ITEMS } from "../content";

describe("PromiseStrip", () => {
  it("renders every promise as a list item without adding headings (keeps heading order clean)", () => {
    render(<PromiseStrip />);
    expect(screen.getAllByRole("listitem")).toHaveLength(PROMISE_ITEMS.length);
    expect(screen.queryAllByRole("heading")).toHaveLength(0);
    for (const item of PROMISE_ITEMS) {
      expect(screen.getByText(item.title)).toBeInTheDocument();
      expect(screen.getByText(item.body)).toBeInTheDocument();
    }
  });
});
