import { describe, expect, it } from "vitest";
import { manifestScanErrorMessage } from "./manifestErrorMessages";

describe("manifestScanErrorMessage", () => {
  it("maps a known reason code to a plain-language message", () => {
    expect(manifestScanErrorMessage("SHIPMENT_NOT_FOUND")).toBe(
      "No shipment matches that AWB."
    );
    expect(manifestScanErrorMessage("ALREADY_RECEIVED")).toBe(
      "This shipment was already received on this manifest."
    );
  });

  it("falls back to a generic message for an unrecognized reason code", () => {
    expect(manifestScanErrorMessage("SOME_FUTURE_CODE")).toBe(
      "Couldn't scan this shipment. Check the AWB and try again."
    );
  });
});
