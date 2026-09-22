import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NotificationTemplatesPanel } from "./NotificationTemplatesPanel";
import { useNotificationTemplatePanelStore } from "@/features/admin/settings/notification-templates/store/notificationTemplatePanelStore";

// Stable reference: the edit sheet's effect depends on `templates`, so a fresh array per render would loop forever.
const templates = vi.hoisted(() => [
  { id: "1", eventType: "shipment.created", channel: "Email", locale: "ne", body: "NE custom {Awb}", isActive: true },
]);

vi.mock("@/features/admin/settings/notification-templates/hooks/useNotificationTemplates", () => ({
  useNotificationTemplates: () => ({ isLoading: false, data: templates }),
}));

function renderPanel() {
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <NotificationTemplatesPanel />
    </QueryClientProvider>
  );
}

describe("NotificationTemplatesPanel", () => {
  beforeEach(() => {
    useNotificationTemplatePanelStore.setState({ panel: { mode: "closed" } });
  });

  it("shows one row per event and language, marking only the Nepali override as custom", () => {
    renderPanel();
    expect(screen.getAllByText("English").length).toBe(5);
    expect(screen.getAllByText("नेपाली").length).toBeGreaterThanOrEqual(5);
    expect(screen.getByText("NE custom {Awb}")).toBeInTheDocument();
    expect(screen.getAllByText("Custom").length).toBe(1);
  });

  it("opens the edit sheet for the clicked language", () => {
    renderPanel();
    fireEvent.click(screen.getAllByRole("button", { name: "Edit" })[1]);
    const panel = useNotificationTemplatePanelStore.getState().panel;
    expect(panel).toMatchObject({ mode: "edit", eventType: "shipment.created", locale: "ne" });
  });
});