import { describe, expect, it } from "vitest";
import { cn } from "./utils";

describe("cn", () => {
  it("keeps the custom text-2xs size alongside a text colour", () => {
    expect(cn("text-2xs", "text-muted-foreground")).toBe("text-2xs text-muted-foreground");
  });

  it("still lets a later font size replace text-2xs", () => {
    expect(cn("text-2xs", "text-sm")).toBe("text-sm");
  });
});
