import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { UserMenu } from "./UserMenu";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

function renderUserMenu() {
  let resolveFetch!: (value: unknown) => void;
  global.fetch = vi.fn().mockReturnValue(
    new Promise((resolve) => {
      resolveFetch = resolve;
    })
  ) as unknown as typeof fetch;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <UserMenu />
    </QueryClientProvider>
  );

  return {
    resolveSession() {
      resolveFetch({
        ok: true,
        status: 200,
        json: async () => ({
          userId: "u1",
          organizationId: "o1",
          email: "a@b.com",
          fullName: "Ram Shrestha",
          permissions: [],
          hasAllBranchesScope: true,
          branches: [],
        }),
      });
    },
  };
}

describe("UserMenu", () => {
  it("shows a skeleton instead of the avatar/name while the session is loading", () => {
    renderUserMenu();

    expect(screen.getByTestId("user-menu-skeleton")).toBeInTheDocument();
    expect(screen.queryByText("Ram Shrestha")).not.toBeInTheDocument();
  });

  it("swaps the skeleton for the avatar/name once the session loads", async () => {
    const { resolveSession } = renderUserMenu();

    resolveSession();

    await waitFor(() => expect(screen.getByText("Ram Shrestha")).toBeInTheDocument());
    expect(screen.queryByTestId("user-menu-skeleton")).not.toBeInTheDocument();
  });
});
