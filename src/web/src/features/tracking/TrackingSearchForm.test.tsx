import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { TrackingSearchForm } from "./TrackingSearchForm";

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: pushMock }) }));

describe("TrackingSearchForm", () => {
  it("shows a validation error for an empty submit", async () => {
    render(<TrackingSearchForm />);

    fireEvent.click(screen.getByRole("button", { name: /track/i }));

    expect(await screen.findByText(/enter an awb number/i)).toBeInTheDocument();
  });

  it("navigates to the AWB's results page on submit", async () => {
    render(<TrackingSearchForm />);

    fireEvent.change(screen.getByLabelText(/awb number/i), { target: { value: "AWB123" } });
    fireEvent.click(screen.getByRole("button", { name: /track/i }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/track/AWB123"));
  });
});
