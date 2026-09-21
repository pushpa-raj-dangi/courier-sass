import { beforeEach, describe, expect, it, vi } from "vitest";
import { waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { readCookie } from "@/i18n/preferences";
import { LocaleSync } from "./LocaleSync";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh }),
}));

function mockSession(locale: unknown) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u", organizationId: "o", organizationName: "Org", email: "a@b.c", fullName: "A B",
      permissions: [], hasAllBranchesScope: true, branches: [], locale,
    }),
  }) as unknown as typeof fetch;
}

function renderSync() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <LocaleSync />
    </QueryClientProvider>,
  );
}

describe("LocaleSync", () => {
  beforeEach(() => {
    refresh.mockClear();
    document.cookie = "courier_locale=; Path=/; Max-Age=0";
    document.cookie = "courier_calendar=; Path=/; Max-Age=0";
  });

  it("seeds both cookies from the tenant default when the user has no preference, then refreshes once", async () => {
    mockSession({ defaultLocale: "ne", calendarPreference: "BikramSambat", timeZoneId: "Asia/Kathmandu" });
    renderSync();

    await waitFor(() => expect(readCookie("courier_locale")).toBe("ne"));
    expect(readCookie("courier_calendar")).toBe("BikramSambat");
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("never overrides a preference the user already chose", async () => {
    document.cookie = "courier_locale=en; Path=/";
    document.cookie = "courier_calendar=Gregorian; Path=/";
    mockSession({ defaultLocale: "ne", calendarPreference: "BikramSambat", timeZoneId: "Asia/Kathmandu" });
    renderSync();

    await new Promise((r) => setTimeout(r, 50));
    expect(readCookie("courier_locale")).toBe("en");
    expect(readCookie("courier_calendar")).toBe("Gregorian");
    expect(refresh).not.toHaveBeenCalled();
  });

  it("ignores unsupported tenant values rather than writing them", async () => {
    mockSession({ defaultLocale: "fr", calendarPreference: "Julian", timeZoneId: "Asia/Kathmandu" });
    renderSync();

    await new Promise((r) => setTimeout(r, 50));
    expect(readCookie("courier_locale")).toBeNull();
    expect(readCookie("courier_calendar")).toBeNull();
    expect(refresh).not.toHaveBeenCalled();
  });

  it("does nothing without a session (401)", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 401, json: async () => null }) as unknown as typeof fetch;
    renderSync();

    await new Promise((r) => setTimeout(r, 50));
    expect(refresh).not.toHaveBeenCalled();
  });
});
