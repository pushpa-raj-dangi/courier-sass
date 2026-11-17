import type { QuoteResponse } from "@/features/merchant-shipments/types";

export interface BookingFigures {
  delivery: number;
  insurance: number;
  charge: number;
  /** COD minus the courier charge; null when the parcel is not COD (nothing to receive). */
  receive: number | null;
}

/** Splits a server quote into the four footer figures. Display only — never sent back. */
export function bookingFigures(quote: QuoteResponse, codAmount: number): BookingFigures {
  const insurance = quote.appliedCharges
    .filter((c) => String(c.type).toLowerCase() === "insurance")
    .reduce((sum, c) => sum + c.amount, 0);
  return {
    delivery: quote.baseRate,
    insurance,
    charge: quote.total,
    receive: codAmount > 0 ? codAmount - quote.total : null,
  };
}

const HINTS: Record<string, string> = {
  standard: "Everyday delivery",
  express: "Faster, priority handling",
  sameday: "Delivered within the day",
  heavy: "Bulky or heavy parcels",
};

/** A short, static description for a service type card; empty when the code is unknown. */
export function serviceTypeHint(code: string): string {
  return HINTS[code.replace(/[\s_-]/g, "").toLowerCase()] ?? "";
}
