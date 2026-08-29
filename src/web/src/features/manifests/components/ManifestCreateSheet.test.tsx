import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ManifestCreateSheet } from "./ManifestCreateSheet";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";
import * as clientFetchModule from "@/lib/api/clientFetch";

function renderWithProviders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ManifestCreateSheet />
    </QueryClientProvider>
  );
}

describe("ManifestCreateSheet", () => {
  it("is not visible when the store is closed", () => {
    useManifestPanelStore.setState({ panel: { mode: "closed" } });
    renderWithProviders();
    expect(screen.queryByText("New Manifest")).not.toBeInTheDocument();
  });

  it("is visible with its fields when the store is open", async () => {
    vi.spyOn(clientFetchModule, "clientFetch").mockResolvedValue([
      { id: "b1", name: "Kathmandu Hub" },
    ]);
    useManifestPanelStore.setState({ panel: { mode: "create" } });
    renderWithProviders();

    await waitFor(() => expect(screen.getByText("New Manifest")).toBeInTheDocument());
    expect(screen.getByLabelText(/from branch/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/to branch/i)).toBeInTheDocument();
  });
});

describe("createManifestSchema", () => {
  it("rejects a From/To branch pair that's the same branch", async () => {
    const { createManifestSchema } = await import("@/features/manifests/schema");
    const result = createManifestSchema.safeParse({
      fromBranchId: "b1",
      toBranchId: "b1",
      purpose: "Forward",
    });
    expect(result.success).toBe(false);
  });

  it("accepts a valid From/To branch pair", async () => {
    const { createManifestSchema } = await import("@/features/manifests/schema");
    const result = createManifestSchema.safeParse({
      fromBranchId: "b1",
      toBranchId: "b2",
      purpose: "Forward",
    });
    expect(result.success).toBe(true);
  });
});
