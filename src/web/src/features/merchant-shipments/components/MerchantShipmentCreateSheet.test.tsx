import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";

let quoteData: unknown;
const mutateAsync = vi.fn();
const close = vi.fn();

vi.mock("@/features/merchant-shipments/store/merchantShipmentPanelStore", () => ({
  useMerchantShipmentPanelStore: (sel: (s: unknown) => unknown) =>
    sel({ panel: { mode: "create" }, close, openDetail: vi.fn() }),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantBranches", () => ({
  useMerchantBranches: () => ({ data: [{ id: "b1", code: "KTM", name: "Kathmandu" }] }),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantServiceTypes", () => ({
  useMerchantServiceTypes: () => ({ data: [{ id: "s1", code: "Express", name: "Express" }] }),
}));
vi.mock("@/features/merchant-shipments/hooks/useMerchantShipmentQuote", () => ({
  useMerchantShipmentQuote: () => ({ data: quoteData, isFetching: false }),
}));
vi.mock("@/features/merchant-shipments/hooks/useCreateMerchantShipment", () => ({
  useCreateMerchantShipment: () => ({ mutateAsync }),
}));
vi.mock("@/features/branches/GeographyPicker", () => ({ GeographyPicker: () => <div data-testid="geo" /> }));

import { MerchantShipmentCreateSheet } from "./MerchantShipmentCreateSheet";

beforeEach(() => {
  quoteData = undefined;
  mutateAsync.mockReset();
});

describe("MerchantShipmentCreateSheet", () => {
  it("renders the title, subtitle and three numbered sections", () => {
    render(<MerchantShipmentCreateSheet />);
    expect(screen.getByText("Book a shipment")).toBeInTheDocument();
    expect(screen.getByText("The price is locked the moment you book.")).toBeInTheDocument();
    expect(screen.getByText("01 · RECIPIENT")).toBeInTheDocument();
    expect(screen.getByText("02 · PARCEL")).toBeInTheDocument();
    expect(screen.getByText("03 · SERVICE AND PAYMENT")).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: /Express/ })).toBeInTheDocument();
  });

  it("disables the book button until a quote exists", () => {
    render(<MerchantShipmentCreateSheet />);
    expect(screen.getByRole("button", { name: "Book shipment" })).toBeDisabled();
  });

  it("shows the quote figures and a priced book button", () => {
    quoteData = { baseRate: 150, appliedCharges: [], discountPercent: 0, subtotal: 150, total: 150 };
    render(<MerchantShipmentCreateSheet />);
    fireEvent.change(screen.getByLabelText("COD amount"), { target: { value: "2500" } });
    expect(screen.getByText("Rs 2,350")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Book for Rs 150" })).toBeEnabled();
  });

  it("reveals the sender fields on change", () => {
    render(<MerchantShipmentCreateSheet />);
    expect(screen.queryByLabelText("Sender name")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "change" }));
    expect(screen.getByLabelText("Sender name")).toBeInTheDocument();
  });

  it("still shows the existing validation messages for an empty submit", async () => {
    const { container } = render(<MerchantShipmentCreateSheet />);
    fireEvent.submit(container.ownerDocument.querySelector("form")!);
    expect(await screen.findByText("Recipient name is required.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText("Service type is required.")).toBeInTheDocument());
    // Sender fields auto-expand so their errors are never hidden behind the summary line.
    expect(await screen.findByText("Sender name is required.")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });
});
