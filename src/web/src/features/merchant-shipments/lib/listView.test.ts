import { describe, expect, it } from "vitest";
import { shipmentsDescription } from "./listView";

describe("shipmentsDescription", () => {
  it("pluralises and joins the two counts", () => {
    expect(shipmentsDescription(10, 5)).toBe("10 shipments booked · 5 on the way");
    expect(shipmentsDescription(1, 0)).toBe("1 shipment booked · 0 on the way");
  });
  it("uses a dash for unknown counts, never 0", () => {
    expect(shipmentsDescription(null, null)).toBe("— shipments booked · — on the way");
  });
});
