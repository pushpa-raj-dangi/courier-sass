import { describe, expect, it } from "vitest";
import { useAdminPanelStore } from "./adminPanelStore";

describe("useAdminPanelStore", () => {
  it("starts closed", () => {
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openRoleDetail sets mode and id, close resets to closed", () => {
    useAdminPanelStore.getState().openRoleDetail("role-1");
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "role-detail", id: "role-1" });
    useAdminPanelStore.getState().close();
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "closed" });
  });

  it("openUserInvite sets mode with no id", () => {
    useAdminPanelStore.getState().openUserInvite();
    expect(useAdminPanelStore.getState().panel).toEqual({ mode: "user-invite" });
  });
});
