import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MerchantLoginForm } from "./MerchantLoginForm";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("MerchantLoginForm", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("shows validation errors for an empty submit", async () => {
    render(<MerchantLoginForm />);

    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/enter a valid email/i)).toBeInTheDocument();
  });

  it("shows a server error on failed login", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      json: async () => ({ error: "Invalid email or password." }),
    });
    render(<MerchantLoginForm />);

    fireEvent.change(screen.getByLabelText(/^email$/i), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "wrong" } });
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText("Invalid email or password.")).toBeInTheDocument());
  });
});
