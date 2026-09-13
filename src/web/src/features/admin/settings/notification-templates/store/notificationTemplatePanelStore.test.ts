import { describe, expect, it, beforeEach } from "vitest";
import { useNotificationTemplatePanelStore } from "./notificationTemplatePanelStore";

describe("useNotificationTemplatePanelStore", () => {
  beforeEach(() => {
    useNotificationTemplatePanelStore.setState({ panel: { mode: "closed" } });
  });

  it("starts closed", () => {
    expect(useNotificationTemplatePanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openEdit switches to the edit panel with the given event type and channel", () => {
    useNotificationTemplatePanelStore.getState().openEdit("shipment.created", "Email");
    expect(useNotificationTemplatePanelStore.getState().panel).toEqual({ mode: "edit", eventType: "shipment.created", channel: "Email" });
  });

  it("close returns to closed from the edit panel", () => {
    useNotificationTemplatePanelStore.getState().openEdit("shipment.created", "Email");
    useNotificationTemplatePanelStore.getState().close();
    expect(useNotificationTemplatePanelStore.getState().panel).toEqual({ mode: "closed" });
  });
});
