import { describe, expect, it } from "vitest";
import { SessionExpiredError } from "@/lib/api/clientFetch";
import { NETWORK_MESSAGE, NOT_FOUND_MESSAGE, scanErrorMessage } from "./scanErrors";

describe("scanErrorMessage", () => {
  it("maps manifest reason codes", () => {
    expect(scanErrorMessage(new Error(JSON.stringify({ error: { reason: "NOT_ON_MANIFEST" } })))).toBe("This shipment isn't on this manifest.");
  });
  it("uses a server error message when present", () => {
    expect(scanErrorMessage(new Error(JSON.stringify({ error: { code: "X", message: "Shipment must be AtDestinationBranch" } })))).toBe(
      "Shipment must be AtDestinationBranch"
    );
  });
  it("maps a 404 to not-found-in-your-branches", () => {
    expect(scanErrorMessage(new Error("Request to /api/shipments/QA failed with 404"))).toBe(NOT_FOUND_MESSAGE);
  });
  it("maps fetch TypeErrors to a network message", () => {
    expect(scanErrorMessage(new TypeError("Failed to fetch"))).toBe(NETWORK_MESSAGE);
  });
  it("maps session expiry", () => {
    expect(scanErrorMessage(new SessionExpiredError())).toMatch(/session expired/i);
  });
  it("falls back to a generic message", () => {
    expect(scanErrorMessage("weird")).toMatch(/couldn't record/i);
  });
});
