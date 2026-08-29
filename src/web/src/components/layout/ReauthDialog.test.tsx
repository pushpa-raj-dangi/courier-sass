import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReauthDialog } from "./ReauthDialog";
import { useAuthStore } from "@/lib/store/authStore";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ReauthDialog />
    </QueryClientProvider>
  );
}

describe("ReauthDialog", () => {
  beforeEach(() => {
    useAuthStore.setState({ sessionExpired: false });
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => null,
    });
  });

  it("is hidden when the session has not expired", () => {
    renderDialog();
    expect(screen.queryByText("Session expired")).not.toBeInTheDocument();
  });

  it("shows when authStore.sessionExpired is true", async () => {
    useAuthStore.setState({ sessionExpired: true });
    renderDialog();
    expect(await screen.findByText("Session expired")).toBeInTheDocument();
  });

  it("hides authStore.sessionExpired on successful re-auth", async () => {
    useAuthStore.setState({ sessionExpired: true });
    (global.fetch as ReturnType<typeof vi.fn>).mockImplementation((url: string) => {
      if (url === "/api/auth/login") {
        return Promise.resolve({ ok: true, status: 200, json: async () => ({ ok: true }) });
      }
      return Promise.resolve({ ok: false, status: 401, json: async () => null });
    });
    renderDialog();

    fireEvent.change(screen.getByLabelText(/^password$/i), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: /^sign in$/i }));

    await waitFor(() => expect(useAuthStore.getState().sessionExpired).toBe(false));
  });
});
