import { describe, expect, it } from "vitest";
import { codErrorMessage } from "./codErrorMessage";

describe("codErrorMessage", () => {
  it("extracts the message from a CodValidationException error body", () => {
    const error = new Error(JSON.stringify({ error: { code: "COD_VALIDATION_FAILED", message: "Cannot approve an adjustment against an already-Settled MerchantSettlement." } }));

    expect(codErrorMessage(error)).toBe("Cannot approve an adjustment against an already-Settled MerchantSettlement.");
  });

  it("falls back to a generic message when the error isn't JSON", () => {
    const error = new Error("Network request failed");

    expect(codErrorMessage(error)).toBe("Something went wrong. Check the fields and try again.");
  });

  it("falls back to a generic message when the JSON has no error.message field", () => {
    const error = new Error(JSON.stringify({ status: 500 }));

    expect(codErrorMessage(error)).toBe("Something went wrong. Check the fields and try again.");
  });
});
