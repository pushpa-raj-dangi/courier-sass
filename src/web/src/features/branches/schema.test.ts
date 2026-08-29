import { describe, expect, it } from "vitest";
import { createBranchSchema } from "./schema";

describe("createBranchSchema", () => {
  it("accepts a fully-filled valid branch", () => {
    const result = createBranchSchema.safeParse({
      code: "KTM-02",
      name: "Kathmandu Branch",
      type: "Branch",
      provinceId: "p1",
      districtId: "d1",
      localLevelId: "l1",
      wardNumber: 5,
      addressLine: "New Road",
    });
    expect(result.success).toBe(true);
  });

  it("rejects a missing geography chain", () => {
    const result = createBranchSchema.safeParse({
      code: "KTM-02",
      name: "Kathmandu Branch",
      type: "Branch",
      provinceId: null,
      districtId: null,
      localLevelId: null,
      wardNumber: null,
      addressLine: "New Road",
    });
    expect(result.success).toBe(false);
  });

  it("rejects a non-positive ward number", () => {
    const result = createBranchSchema.safeParse({
      code: "KTM-02",
      name: "Kathmandu Branch",
      type: "Branch",
      provinceId: "p1",
      districtId: "d1",
      localLevelId: "l1",
      wardNumber: 0,
      addressLine: "New Road",
    });
    expect(result.success).toBe(false);
  });
});
