import { describe, expect, it, beforeEach } from "vitest";
import { useCommandPaletteStore } from "./commandPaletteStore";

describe("useCommandPaletteStore", () => {
  beforeEach(() => {
    useCommandPaletteStore.setState({ open: false });
  });

  it("starts closed", () => {
    expect(useCommandPaletteStore.getState().open).toBe(false);
  });

  it("setOpen sets the open state directly", () => {
    useCommandPaletteStore.getState().setOpen(true);
    expect(useCommandPaletteStore.getState().open).toBe(true);
  });

  it("toggle flips the open state", () => {
    useCommandPaletteStore.getState().toggle();
    expect(useCommandPaletteStore.getState().open).toBe(true);

    useCommandPaletteStore.getState().toggle();
    expect(useCommandPaletteStore.getState().open).toBe(false);
  });
});
