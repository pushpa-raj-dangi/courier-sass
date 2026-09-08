import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { RegisterForm } from "./RegisterForm";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("RegisterForm", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("shows validation errors for an empty submit", async () => {
    render(<RegisterForm />);

    fireEvent.click(screen.getByRole("button", { name: /create organization/i }));

    expect(await screen.findByText(/organization name must be at least 2 characters/i)).toBeInTheDocument();
  });

  it("auto-fills the slug from the organization name until the slug is edited by hand", () => {
    render(<RegisterForm />);

    fireEvent.change(screen.getByLabelText(/organization name/i), { target: { value: "Kathmandu Express" } });
    expect(screen.getByLabelText(/organization slug/i)).toHaveValue("kathmandu-express");

    fireEvent.change(screen.getByLabelText(/organization slug/i), { target: { value: "kex" } });
    fireEvent.change(screen.getByLabelText(/organization name/i), { target: { value: "Kathmandu Express Pvt" } });
    expect(screen.getByLabelText(/organization slug/i)).toHaveValue("kex");
  });

  it("flags mismatched passwords", async () => {
    render(<RegisterForm />);

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "password123" } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: "different123" } });
    fireEvent.click(screen.getByRole("button", { name: /create organization/i }));

    expect(await screen.findByText(/passwords don't match/i)).toBeInTheDocument();
  });

  it("shows a server error on failed registration", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      json: async () => ({ error: "Email is already registered." }),
    });
    render(<RegisterForm />);

    fireEvent.change(screen.getByLabelText(/organization name/i), { target: { value: "Kathmandu Express" } });
    fireEvent.change(screen.getByLabelText(/your full name/i), { target: { value: "Anish Rai" } });
    fireEvent.change(screen.getByLabelText(/^email$/i), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "password123" } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: "password123" } });
    fireEvent.click(screen.getByRole("button", { name: /create organization/i }));

    await waitFor(() => expect(screen.getByText("Email is already registered.")).toBeInTheDocument());
  });
});
