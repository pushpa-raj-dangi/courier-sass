import { describe, expect, it, vi, beforeEach } from "vitest";
import { screen, fireEvent, waitFor } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { LoginForm } from "./LoginForm";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("LoginForm", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("shows validation errors for an empty submit", async () => {
    renderWithIntl(<LoginForm />);

    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    expect(await screen.findByText(/enter a valid email/i)).toBeInTheDocument();
  });

  it("shows a server error on failed login", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      json: async () => ({ error: "Invalid email or password." }),
    });
    renderWithIntl(<LoginForm />);

    fireEvent.change(screen.getByLabelText(/^email$/i), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "wrong" } });
    fireEvent.click(screen.getByRole("button", { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText("Invalid email or password.")).toBeInTheDocument());
  });

  it("shows validation errors in Nepali", async () => {
    renderWithIntl(<LoginForm />, { locale: "ne" });

    fireEvent.click(screen.getByRole("button", { name: "साइन इन" }));

    expect(await screen.findByText("मान्य इमेल ठेगाना लेख्नुहोस्।")).toBeInTheDocument();
    expect(screen.getByText("पासवर्ड आवश्यक छ।")).toBeInTheDocument();
  });

  it("falls back to the translated generic error when the server sends none", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({ ok: false, json: async () => ({}) });
    renderWithIntl(<LoginForm />, { locale: "ne" });

    fireEvent.change(screen.getByLabelText("इमेल"), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText("पासवर्ड"), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: "साइन इन" }));

    expect(await screen.findByText("लगइन असफल भयो।")).toBeInTheDocument();
  });
});
