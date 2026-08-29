import { describe, expect, it, beforeEach } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { useQuery } from "@tanstack/react-query";
import { Providers } from "./providers";
import { SessionExpiredError } from "@/lib/api/clientFetch";
import { useAuthStore } from "@/lib/store/authStore";

function ThrowingQuery() {
  useQuery({
    queryKey: ["throws-session-expired"],
    queryFn: () => {
      throw new SessionExpiredError();
    },
    retry: false,
  });
  return null;
}

describe("Providers", () => {
  beforeEach(() => {
    useAuthStore.setState({ sessionExpired: false });
  });

  it("flips authStore.sessionExpired when a query throws SessionExpiredError", async () => {
    render(
      <Providers>
        <ThrowingQuery />
      </Providers>
    );

    await waitFor(() => expect(useAuthStore.getState().sessionExpired).toBe(true));
  });
});
