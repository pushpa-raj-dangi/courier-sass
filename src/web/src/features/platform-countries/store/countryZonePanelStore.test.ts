import { describe, expect, it, beforeEach } from "vitest";
import { useCountryZonePanelStore } from "./countryZonePanelStore";

describe("countryZonePanelStore", () => {
  beforeEach(() => {
    useCountryZonePanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openCreate opens create mode", () => {
    useCountryZonePanelStore.getState().openCreate();
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "create" });
  });

  it("openEdit opens edit mode with the zone id", () => {
    useCountryZonePanelStore.getState().openEdit("zone-1");
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "edit", zoneId: "zone-1" });
  });

  it("close returns to closed", () => {
    useCountryZonePanelStore.getState().openCreate();
    useCountryZonePanelStore.getState().close();
    expect(useCountryZonePanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
