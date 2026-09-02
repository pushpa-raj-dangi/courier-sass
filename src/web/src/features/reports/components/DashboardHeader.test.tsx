import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DashboardHeader } from "./DashboardHeader";

function mockSession(session: unknown) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: session !== null,
    status: session === null ? 401 : 200,
    json: async () => session,
  }) as unknown as typeof fetch;
}

function renderHeader() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <DashboardHeader />
    </QueryClientProvider>
  );
}

describe("DashboardHeader", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows the organization's real name once the session loads", async () => {
    vi.setSystemTime(new Date(2026, 7, 15, 9, 0));
    mockSession({
      userId: "u1",
      organizationId: "o1",
      organizationName: "Everest Express Courier",
      email: "a@b.com",
      fullName: "Anita Rai",
      permissions: [],
      hasAllBranchesScope: true,
      branches: [],
    });
    renderHeader();
    await waitFor(() => expect(screen.getByText("Everest Express Courier")).toBeInTheDocument());
  });

  it("greets by first name only, and picks a morning greeting before noon", async () => {
    vi.setSystemTime(new Date(2026, 7, 15, 9, 0));
    mockSession({
      userId: "u1",
      organizationId: "o1",
      organizationName: "Everest Express Courier",
      email: "a@b.com",
      fullName: "Anita Rai",
      permissions: [],
      hasAllBranchesScope: true,
      branches: [],
    });
    renderHeader();
    await waitFor(() => expect(screen.getByText("Good morning, Anita")).toBeInTheDocument());
  });

  it("picks an evening greeting after 5pm", async () => {
    vi.setSystemTime(new Date(2026, 7, 15, 19, 0));
    mockSession({
      userId: "u1",
      organizationId: "o1",
      organizationName: "Everest Express Courier",
      email: "a@b.com",
      fullName: "Anita Rai",
      permissions: [],
      hasAllBranchesScope: true,
      branches: [],
    });
    renderHeader();
    await waitFor(() => expect(screen.getByText("Good evening, Anita")).toBeInTheDocument());
  });

  it("falls back to a generic title while there is no session yet", () => {
    mockSession(null);
    renderHeader();
    expect(screen.getByText("Organization Dashboard")).toBeInTheDocument();
  });
});
