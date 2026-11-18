import { describe, expect, it } from "vitest";
import { AVAILABLE_SCOPES, SCOPE_LABELS, scopeLabel } from "./scopeLabels";

describe("scopeLabels", () => {
  it("gives every available scope a non-empty label distinct from the raw scope", () => {
    expect(AVAILABLE_SCOPES).toHaveLength(15);
    for (const scope of AVAILABLE_SCOPES) {
      expect(SCOPE_LABELS[scope]).toBeTruthy();
      expect(SCOPE_LABELS[scope]).not.toBe(scope);
      expect(scopeLabel(scope)).toBe(SCOPE_LABELS[scope]);
    }
  });

  it("maps known scopes to plain language", () => {
    expect(scopeLabel("shipment.create")).toBe("Book shipments");
    expect(scopeLabel("merchant.cod.read")).toBe("View COD");
  });

  it("falls back to the raw scope when unknown", () => {
    expect(scopeLabel("x.unknown")).toBe("x.unknown");
  });

  it("never offers the api-keys manage scope", () => {
    expect(AVAILABLE_SCOPES).not.toContain("merchant.apikeys.manage");
  });
});
