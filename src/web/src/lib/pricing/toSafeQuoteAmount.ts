// Both the staff ShipmentCreateSheet and the merchant MerchantShipmentCreateSheet build a live
// quote-preview request from React Hook Form's `watch()` values while the user is still typing —
// before Zod validation runs. A numeric field registered with `valueAsNumber: true` (codAmount)
// yields NaN, not undefined/null, when its input is empty — a genuinely common case, since most
// shipments carry no COD and the field has no default value. `codAmount ?? 0` only coalesces
// null/undefined and lets NaN through unchanged, and JSON.stringify serializes NaN as the literal
// `null`, which crashes the backend's PriceQuoteRequest.CodAmount (a non-nullable decimal) with a
// 500 — found via Frontend Phase F10 Task 16's live E2E pass leaving COD Amount blank while
// booking a plain (non-COD) shipment.
export function toSafeQuoteAmount(value: number | null | undefined): number {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return 0;
  }
  return value;
}
