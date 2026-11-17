import { describe, expect, it } from "vitest";
import { codLine } from "./codLine";

const base = { settlementId: null, periodFrom: null, periodTo: null, settledAt: null };

describe("codLine", () => {
  it("is Prepaid when the COD amount is zero, whatever the status", () => {
    expect(codLine(undefined, 0)).toBe("Prepaid");
    expect(codLine({ ...base, state: "NotCollected" }, 0)).toBe("Prepaid");
  });
  it("is an em dash while the status has not loaded", () => {
    expect(codLine(undefined, 2500)).toBe("—");
  });
  it("describes each state", () => {
    expect(codLine({ ...base, state: "NotCollected" }, 2500)).toBe("Not yet collected");
    expect(codLine({ ...base, state: "CollectedAwaitingSettlement" }, 2500)).toBe("Collected, awaiting settlement");
    expect(codLine({ ...base, state: "Prepaid" }, 2500)).toBe("Prepaid");
  });
  it("shows the settlement period while in settlement", () => {
    const out = codLine(
      { ...base, state: "InSettlement", periodFrom: "2026-10-01T00:00:00Z", periodTo: "2026-10-15T00:00:00Z" },
      2500,
    );
    expect(out).toMatch(/^In settlement \(.+–.+\)$/);
  });
  it("shows the settled date once settled", () => {
    expect(codLine({ ...base, state: "Settled", settledAt: "2026-10-20T05:00:00Z" }, 2500)).toMatch(/^Settled on /);
  });
});
