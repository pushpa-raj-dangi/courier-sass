import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const clientFetchMock = vi.fn();
vi.mock("@/lib/api/clientFetch", () => ({ clientFetch: (...a: unknown[]) => clientFetchMock(...a) }));

let canManage = true;
vi.mock("@/features/merchant-auth/hooks/useMerchantPermission", () => ({
  useMerchantPermission: (p: string) => (p === "merchant.apikeys.manage" ? canManage : true),
}));

import { useMerchantApiKeyPanelStore } from "@/features/merchant-api-keys/store/merchantApiKeyPanelStore";
import { MerchantApiKeysPage } from "./MerchantApiKeysPage";
import type { ApiKey } from "../types";

const recent = new Date(Date.now() - 12 * 60_000).toISOString();
const old = "2026-01-05T00:00:00Z";

const keys: ApiKey[] = [
  { id: "k1", prefix: "ck_live_ab12", scopes: ["shipment.create", "shipment.read"], isActive: true, createdAt: "2026-03-01T00:00:00Z", revokedAt: null, lastUsedAt: recent },
  { id: "k2", prefix: "ck_live_cd34", scopes: ["shipment.cancel"], isActive: true, createdAt: "2026-02-01T00:00:00Z", revokedAt: null, lastUsedAt: old },
  { id: "k3", prefix: "ck_live_ef56", scopes: ["merchant.cod.read"], isActive: false, createdAt: "2026-01-01T00:00:00Z", revokedAt: "2026-02-10T00:00:00Z", lastUsedAt: null },
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MerchantApiKeysPage />
    </QueryClientProvider>
  );
}

describe("MerchantApiKeysPage", () => {
  beforeEach(() => {
    clientFetchMock.mockReset();
    canManage = true;
    useMerchantApiKeyPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("shows the no-access state and never fetches without merchant.apikeys.manage", () => {
    canManage = false;
    renderPage();
    expect(screen.getByText("You don't have access to this page")).toBeInTheDocument();
    expect(clientFetchMock).not.toHaveBeenCalled();
  });

  it("renders subtitle, tabs with counts, and active rows with scope labels", async () => {
    clientFetchMock.mockResolvedValue(keys);
    renderPage();

    expect(await screen.findByText("ck_live_ab12…")).toBeInTheDocument();
    expect(
      screen.getByText("Keys let your store or software book shipments through the Merchant API")
    ).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /Active/ })).toHaveTextContent("2");
    expect(screen.getByRole("tab", { name: /Revoked/ })).toHaveTextContent("1");

    expect(screen.getByText("Book shipments")).toBeInTheDocument();
    expect(screen.getByText("Read shipments")).toBeInTheDocument();
    expect(screen.getByText("Cancel shipments")).toBeInTheDocument();
    expect(screen.queryByText("ck_live_ef56…")).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Revoke" })).toHaveLength(2);
  });

  it("shows revoked keys with a Revoked date and no Revoke button", async () => {
    clientFetchMock.mockResolvedValue(keys);
    renderPage();
    await screen.findByText("ck_live_ab12…");

    fireEvent.mouseDown(screen.getByRole("tab", { name: /Revoked/ }), { button: 0 });
    fireEvent.click(screen.getByRole("tab", { name: /Revoked/ }));

    const row = (await screen.findByText("ck_live_ef56…")).closest("tr") as HTMLElement;
    expect(within(row).getByText(/^Revoked /)).toBeInTheDocument();
    expect(within(row).queryByRole("button", { name: "Revoke" })).not.toBeInTheDocument();
  });

  it("confirms before revoking and mentions recent use", async () => {
    clientFetchMock.mockResolvedValue(keys);
    renderPage();
    await screen.findByText("ck_live_ab12…");

    const row = screen.getByText("ck_live_ab12…").closest("tr") as HTMLElement;
    fireEvent.click(within(row).getByRole("button", { name: "Revoke" }));

    const dialog = await screen.findByRole("alertdialog");
    expect(within(dialog).getByText("Revoke key ck_live_ab12…?")).toBeInTheDocument();
    expect(within(dialog).getByText(/Anything using this key stops working right away\./)).toBeInTheDocument();
    expect(within(dialog).getByText(/This can't be undone\./)).toBeInTheDocument();
    expect(within(dialog).getByText(/It was last used 12 min ago, so something is probably still calling it\./)).toBeInTheDocument();
  });

  it("omits the recent-use sentence for a key not used within 24 h", async () => {
    clientFetchMock.mockResolvedValue(keys);
    renderPage();
    await screen.findByText("ck_live_cd34…");

    const row = screen.getByText("ck_live_cd34…").closest("tr") as HTMLElement;
    fireEvent.click(within(row).getByRole("button", { name: "Revoke" }));

    const dialog = await screen.findByRole("alertdialog");
    expect(within(dialog).queryByText(/probably still calling it/)).not.toBeInTheDocument();
  });

  it("Keep key closes without a request", async () => {
    clientFetchMock.mockResolvedValue(keys);
    renderPage();
    await screen.findByText("ck_live_ab12…");
    clientFetchMock.mockClear();

    const row = screen.getByText("ck_live_ab12…").closest("tr") as HTMLElement;
    fireEvent.click(within(row).getByRole("button", { name: "Revoke" }));
    fireEvent.click(await screen.findByRole("button", { name: "Keep key" }));

    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    expect(clientFetchMock).not.toHaveBeenCalled();
  });

  it("Revoke key calls the revoke endpoint", async () => {
    clientFetchMock.mockResolvedValue(keys);
    renderPage();
    await screen.findByText("ck_live_ab12…");
    clientFetchMock.mockClear();
    clientFetchMock.mockImplementation((_url: string, init?: { method?: string }) =>
      Promise.resolve(init?.method === "DELETE" ? {} : keys)
    );

    const row = screen.getByText("ck_live_ab12…").closest("tr") as HTMLElement;
    fireEvent.click(within(row).getByRole("button", { name: "Revoke" }));
    fireEvent.click(await screen.findByRole("button", { name: "Revoke key" }));

    await waitFor(() =>
      expect(clientFetchMock).toHaveBeenCalledWith("/api/merchant/api-keys/k1", { method: "DELETE" })
    );
  });

  it("opens the create sheet from the header action", async () => {
    clientFetchMock.mockResolvedValue(keys);
    renderPage();
    await screen.findByText("ck_live_ab12…");
    fireEvent.click(screen.getByRole("button", { name: /Create API key/i }));
    expect(useMerchantApiKeyPanelStore.getState().panel.mode).toBe("create");
  });
});
