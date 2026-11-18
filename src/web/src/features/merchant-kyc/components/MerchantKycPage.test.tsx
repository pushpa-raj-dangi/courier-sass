import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const clientFetchMock = vi.fn();
vi.mock("@/lib/api/clientFetch", () => ({ clientFetch: (...a: unknown[]) => clientFetchMock(...a) }));

import { useMerchantKycPanelStore } from "@/features/merchant-kyc/store/merchantKycPanelStore";
import { MerchantKycPage } from "./MerchantKycPage";
import type { MerchantKycDocument } from "../types";

const doc = (over: Partial<MerchantKycDocument>): MerchantKycDocument => ({
  id: "d",
  documentType: "CitizenshipDocument",
  status: "Approved",
  uploadedAt: "2026-11-02T06:00:00Z",
  reviewedAt: "2026-11-03T06:00:00Z",
  rejectionReason: null,
  ...over,
});

const docs: MerchantKycDocument[] = [
  doc({ id: "c", documentType: "CitizenshipDocument" }),
  doc({ id: "p", documentType: "PanCard" }),
  doc({
    id: "b",
    documentType: "BusinessRegistrationCertificate",
    status: "Rejected",
    uploadedAt: "2026-11-14T06:00:00Z",
    reviewedAt: "2026-11-15T06:00:00Z",
    rejectionReason: "The registration number is cut off.",
  }),
  doc({ id: "k", documentType: "BankAccountProof", status: "Pending", uploadedAt: null, reviewedAt: null }),
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MerchantKycPage />
    </QueryClientProvider>
  );
}

describe("MerchantKycPage", () => {
  const openUpload = vi.fn();

  beforeEach(() => {
    clientFetchMock.mockReset();
    openUpload.mockReset();
    useMerchantKycPanelStore.setState({ openUpload });
  });

  it("renders the four required rows in order with state chips and the progress band", async () => {
    clientFetchMock.mockResolvedValue(docs);
    renderPage();

    expect(await screen.findByText("2 of 4 documents approved")).toBeInTheDocument();
    expect(screen.getByText("1 needs a new upload · 1 waiting for review")).toBeInTheDocument();

    const rows = screen.getAllByTestId("kyc-row");
    expect(rows.map((r) => within(r).getByTestId("kyc-row-name").textContent)).toEqual([
      "Citizenship document",
      "PAN card",
      "Business registration certificate",
      "Bank account proof",
    ]);
    expect(within(rows[0]).getByText("Approved")).toBeInTheDocument();
    expect(within(rows[2]).getByText("Rejected")).toBeInTheDocument();
    expect(within(rows[3]).getByText("In review")).toBeInTheDocument();
  });

  it("shows upload and review dates, and a dash when uploadedAt is null", async () => {
    clientFetchMock.mockResolvedValue(docs);
    renderPage();
    const rows = await screen.findAllByTestId("kyc-row");
    expect(within(rows[0]).getByText(/^Uploaded .+ · approved .+$/)).toBeInTheDocument();
    expect(within(rows[2]).getByText(/^Uploaded .+ · rejected .+$/)).toBeInTheDocument();
    expect(within(rows[3]).getByText("Uploaded —")).toBeInTheDocument();
  });

  it("shows the courier note and an Upload again button for a rejected row", async () => {
    clientFetchMock.mockResolvedValue(docs);
    renderPage();
    const rows = await screen.findAllByTestId("kyc-row");
    expect(within(rows[2]).getByText("Courier note: The registration number is cut off.")).toBeInTheDocument();
    fireEvent.click(within(rows[2]).getByRole("button", { name: "Upload again" }));
    expect(openUpload).toHaveBeenCalledWith("BusinessRegistrationCertificate");
  });

  it("offers Upload on a not-uploaded row and none on approved or in-review rows", async () => {
    clientFetchMock.mockResolvedValue([doc({ id: "c", documentType: "CitizenshipDocument" })]);
    renderPage();
    const rows = await screen.findAllByTestId("kyc-row");
    expect(within(rows[0]).queryByRole("button")).toBeNull();
    expect(within(rows[1]).getByText("Not uploaded")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Not uploaded yet")).toBeInTheDocument();
    expect(screen.queryByText("Required for COD payouts")).toBeNull();
    fireEvent.click(within(rows[1]).getByRole("button", { name: "Upload" }));
    expect(openUpload).toHaveBeenCalledWith("PanCard");
  });

  it("opens the upload Sheet with Other from the footer row", async () => {
    clientFetchMock.mockResolvedValue(docs);
    renderPage();
    fireEvent.click(await screen.findByRole("button", { name: "Upload other document" }));
    expect(openUpload).toHaveBeenCalledWith("Other");
  });

  it("lists uploaded Other documents below the required rows", async () => {
    clientFetchMock.mockResolvedValue([...docs, doc({ id: "o", documentType: "Other", status: "Pending" })]);
    renderPage();
    const rows = await screen.findAllByTestId("kyc-row");
    expect(rows).toHaveLength(5);
    expect(within(rows[4]).getByTestId("kyc-row-name")).toHaveTextContent("Other");
  });

  it("shows an error state with retry", async () => {
    clientFetchMock.mockRejectedValue(new Error("boom"));
    renderPage();
    expect(await screen.findByText("Couldn't load KYC documents")).toBeInTheDocument();
  });
});
