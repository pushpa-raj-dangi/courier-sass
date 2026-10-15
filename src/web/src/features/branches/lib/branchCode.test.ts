import { describe, expect, it } from "vitest";
import { branchCode } from "./branchCode";
import type { Branch } from "@/features/branches/types";

const branches = [
  { id: "b1", code: "KTM", name: "Kathmandu Hub" },
  { id: "b2", code: "  ", name: "Pokhara Hub" },
] as Branch[];

describe("branchCode", () => {
  it("prefers the code, falls back to the name, and shows a dash when unknown", () => {
    expect(branchCode(branches, "b1")).toBe("KTM");
    expect(branchCode(branches, "b2")).toBe("Pokhara Hub");
    expect(branchCode(branches, "missing")).toBe("—");
    expect(branchCode(branches, null)).toBe("—");
    expect(branchCode(undefined, "b1")).toBe("—");
  });
});
