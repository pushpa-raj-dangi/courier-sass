import { describe, expect, it } from "vitest";
import { createDeduper } from "./dedupe";

describe("createDeduper", () => {
  it("suppresses the same code inside the window and allows it after", () => {
    let t = 0;
    const accept = createDeduper(3000, () => t);
    expect(accept("A")).toBe(true);
    t = 1000;
    expect(accept("A")).toBe(false);
    t = 2999;
    expect(accept("A")).toBe(false);
    t = 4001; // window restarts from the last *seen* frame, so a held barcode stays suppressed
    expect(accept("A")).toBe(false);
    t = 7002;
    expect(accept("A")).toBe(true);
  });

  it("lets a different code through immediately", () => {
    let t = 0;
    const accept = createDeduper(3000, () => t);
    expect(accept("A")).toBe(true);
    t = 10;
    expect(accept("B")).toBe(true);
    t = 20;
    expect(accept("A")).toBe(true);
  });
});
