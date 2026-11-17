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

  it("marks the input invalid and gives the wrapper an error border", async () => {
    render(<TrackingSearchForm />);
    const input = screen.getByLabelText("AWB number");
    expect(input).toHaveAttribute("aria-invalid", "false");
    expect(input.parentElement).not.toHaveClass("border-destructive");

    fireEvent.click(screen.getByRole("button", { name: /track/i }));

    await screen.findByText(/enter an awb number/i);
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input.parentElement).toHaveClass("border-destructive");
    expect(input.parentElement).toHaveAttribute("data-invalid", "true");
  });

  it("navigates to the AWB's results page on submit", async () => {
    render(<TrackingSearchForm />);

    fireEvent.change(screen.getByLabelText(/awb number/i), { target: { value: "AWB123" } });
    fireEvent.click(screen.getByRole("button", { name: /track/i }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/track/AWB123"));
  });

  it("prefills defaultAwb", () => {
    render(<TrackingSearchForm defaultAwb="NX-1" />);

    expect(screen.getByLabelText("AWB number")).toHaveValue("NX-1");
  });

  it("trims and encodes on submit", async () => {
    pushMock.mockClear();
    render(<TrackingSearchForm />);

    fireEvent.change(screen.getByLabelText("AWB number"), { target: { value: " ab 1 " } });
    fireEvent.click(screen.getByRole("button", { name: /track/i }));

    await waitFor(() => expect(pushMock).toHaveBeenCalledWith("/track/ab%201"));
  });
});
