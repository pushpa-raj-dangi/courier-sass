import { describe, expect, it } from "vitest";
import { initials, roleCounts } from "./staffView";
import type { MerchantStaffMember } from "../types";

const m = (over: Partial<MerchantStaffMember>): MerchantStaffMember => ({
  id: "x",
  email: "x@e.com",
  fullName: "X",
  role: "Staff",
  isActive: true,
  ...over,
});

describe("roleCounts", () => {
  it("counts every row by role, active or not", () => {
    const staff = [
      m({ role: "Owner" }),
      m({ id: "a" }),
      m({ id: "b" }),
      m({ id: "c", isActive: false }),
    ];
    expect(roleCounts(staff)).toEqual({ Owner: 1, Staff: 3 });
  });
});

describe("initials", () => {
  it("uses first letters of first and last words", () => {
    expect(initials("Sita Kumari Rai")).toBe("SR");
    expect(initials("sita rai")).toBe("SR");
  });
  it("handles one word and blank", () => {
    expect(initials("Ram")).toBe("R");
    expect(initials("  ")).toBe("?");
  });
});
