import { describe, expect, it, vi, beforeEach } from "vitest";
import { screen, fireEvent, waitFor } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { RegisterForm } from "./RegisterForm";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

describe("RegisterForm", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("shows validation errors for an empty submit", async () => {
    renderWithIntl(<RegisterForm />);

    fireEvent.click(screen.getByRole("button", { name: /create organization/i }));

    expect(await screen.findByText(/organization name must be at least 2 characters/i)).toBeInTheDocument();
  });

  it("auto-fills the slug from the organization name until the slug is edited by hand", () => {
    renderWithIntl(<RegisterForm />);

    fireEvent.change(screen.getByLabelText(/organization name/i), { target: { value: "Kathmandu Express" } });
    expect(screen.getByLabelText(/organization slug/i)).toHaveValue("kathmandu-express");

    fireEvent.change(screen.getByLabelText(/organization slug/i), { target: { value: "kex" } });
    fireEvent.change(screen.getByLabelText(/organization name/i), { target: { value: "Kathmandu Express Pvt" } });
    expect(screen.getByLabelText(/organization slug/i)).toHaveValue("kex");
  });

  it("flags mismatched passwords", async () => {
    renderWithIntl(<RegisterForm />);

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
    renderWithIntl(<RegisterForm />);

    fireEvent.change(screen.getByLabelText(/organization name/i), { target: { value: "Kathmandu Express" } });
    fireEvent.change(screen.getByLabelText(/your full name/i), { target: { value: "Anish Rai" } });
    fireEvent.change(screen.getByLabelText(/^email$/i), { target: { value: "a@b.com" } });
    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "password123" } });
    fireEvent.change(screen.getByLabelText(/confirm password/i), { target: { value: "password123" } });
    fireEvent.click(screen.getByRole("button", { name: /create organization/i }));

    await waitFor(() => expect(screen.getByText("Email is already registered.")).toBeInTheDocument());
  });

  describe("in Nepali", () => {
    const submitName = "संस्था सिर्जना गर्नुहोस्";

    function fillValid() {
      fireEvent.change(screen.getByLabelText("संस्थाको नाम"), { target: { value: "Kathmandu Express" } });
      fireEvent.change(screen.getByLabelText("तपाईंको पूरा नाम"), { target: { value: "Anish Rai" } });
      fireEvent.change(screen.getByLabelText("इमेल"), { target: { value: "a@b.com" } });
      fireEvent.change(screen.getByLabelText("पासवर्ड"), { target: { value: "password123" } });
      fireEvent.change(screen.getByLabelText("पासवर्ड पुष्टि गर्नुहोस्"), { target: { value: "password123" } });
    }

    it("shows Nepali validation messages for an empty submit", async () => {
      renderWithIntl(<RegisterForm />, { locale: "ne" });

      fireEvent.click(screen.getByRole("button", { name: submitName }));

      expect(await screen.findByText("संस्थाको नाम कम्तीमा २ अक्षरको हुनुपर्छ।")).toBeInTheDocument();
      expect(screen.getByText("पासवर्ड कम्तीमा ८ अक्षरको हुनुपर्छ।")).toBeInTheDocument();
    });

    it("flags mismatched passwords in Nepali", async () => {
      renderWithIntl(<RegisterForm />, { locale: "ne" });

      fireEvent.change(screen.getByLabelText("पासवर्ड"), { target: { value: "password123" } });
      fireEvent.change(screen.getByLabelText("पासवर्ड पुष्टि गर्नुहोस्"), { target: { value: "different123" } });
      fireEvent.click(screen.getByRole("button", { name: submitName }));

      expect(await screen.findByText("पासवर्डहरू मेल खाँदैनन्।")).toBeInTheDocument();
    });

    it("translates the fallback server error but shows a server-supplied one verbatim", async () => {
      const fetchMock = global.fetch as ReturnType<typeof vi.fn>;
      fetchMock.mockResolvedValueOnce({ ok: false, json: async () => ({}) });
      renderWithIntl(<RegisterForm />, { locale: "ne" });

      fillValid();
      fireEvent.click(screen.getByRole("button", { name: submitName }));
      await waitFor(() => expect(screen.getByText("दर्ता असफल भयो।")).toBeInTheDocument());

      fetchMock.mockResolvedValueOnce({ ok: false, json: async () => ({ error: "Slug taken." }) });
      fireEvent.click(screen.getByRole("button", { name: submitName }));
      await waitFor(() => expect(screen.getByText("Slug taken.")).toBeInTheDocument());
    });

    it("renders Nepali labels and no raw message keys", async () => {
      const { container } = renderWithIntl(<RegisterForm />, { locale: "ne" });

      expect(screen.getByLabelText("संस्थाको नाम")).toBeInTheDocument();
      expect(screen.getByLabelText("पासवर्ड")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: submitName }));
      await screen.findByText("संस्थाको नाम कम्तीमा २ अक्षरको हुनुपर्छ।");

      expect(container.textContent).not.toMatch(
        /organizationNameMin|slugMin|slugFormat|fullNameRequired|emailInvalid|passwordMin|confirmRequired|passwordsMismatch/,
      );
    });
  });
});
