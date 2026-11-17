import { describe, expect, it } from "vitest";
import { bookingFigures, serviceTypeHint } from "./bookingFigures";

const quote = { baseRate: 120, appliedCharges: [{ type: "Insurance", amount: 30 }, { type: "Fuel", amount: 5 }], discountPercent: 0, subtotal: 155, total: 155 };

describe("bookingFigures", () => {
  it("splits delivery, insurance and what the merchant receives", () => {
    expect(bookingFigures(quote, 2500)).toEqual({ delivery: 120, insurance: 30, charge: 155, receive: 2345 });
  });
  it("has nothing to receive for a non-COD parcel", () => {
    expect(bookingFigures(quote, 0).receive).toBeNull();
  });
});

describe("serviceTypeHint", () => {
  it("matches codes loosely and falls back to empty", () => {
    expect(serviceTypeHint("Same Day")).toBe("Delivered within the day");
    expect(serviceTypeHint("Mystery")).toBe("");
  });
});
