import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

vi.mock("@/lib/api/clientFetch", () => ({ clientFetch: vi.fn() }));

import { useMerchantKycPanelStore } from "@/features/merchant-kyc/store/merchantKycPanelStore";
import { MerchantKycUploadSheet } from "./MerchantKycUploadSheet";

function renderSheet() {
  const client = new QueryClient();
  return render(
    <QueryClientProvider client={client}>
      <MerchantKycUploadSheet />
    </QueryClientProvider>
  );
}

describe("MerchantKycUploadSheet", () => {
  beforeEach(() => {
    useMerchantKycPanelStore.setState({ panel: { mode: "closed" } });
  });

  it("preselects the document type the caller opened it with", () => {
    renderSheet();
    act(() => useMerchantKycPanelStore.getState().openUpload("PanCard"));
    expect(screen.getByRole("combobox", { name: "Document type" })).toHaveTextContent("PAN Card");
  });

  it("re-initialises the type on a later open", () => {
    renderSheet();
    act(() => useMerchantKycPanelStore.getState().openUpload("PanCard"));
    act(() => useMerchantKycPanelStore.getState().close());
    act(() => useMerchantKycPanelStore.getState().openUpload("BankAccountProof"));
    expect(screen.getByRole("combobox", { name: "Document type" })).toHaveTextContent("Bank Account Proof");
  });
});
