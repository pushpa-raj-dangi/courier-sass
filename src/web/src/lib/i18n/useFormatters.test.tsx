import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { IntlTestProvider } from "@/test/renderWithIntl";
import { PreferencesProvider } from "@/i18n/PreferencesProvider";
import { useFormatters } from "./useFormatters";

function Probe({ instant }: { instant: string }) {
  const { formatDate, formatMoney } = useFormatters();
  return (
    <div>
      <span data-testid="date">{formatDate(instant)}</span>
      <span data-testid="money">{formatMoney(1250, "NPR").replace(/[  ]/g, " ")}</span>
    </div>
  );
}

function renderProbe(options: { locale: "en" | "ne"; calendar: "Gregorian" | "BikramSambat"; timeZoneId?: string }) {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => ({
      userId: "u",
      organizationId: "o",
      organizationName: "Org",
      email: "a@b.c",
      fullName: "A B",
      permissions: [],
      hasAllBranchesScope: true,
      branches: [],
      locale: { defaultLocale: options.locale, calendarPreference: options.calendar, timeZoneId: options.timeZoneId ?? "Asia/Kathmandu" },
    }),
  }) as unknown as typeof fetch;
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <IntlTestProvider locale={options.locale}>
        <PreferencesProvider calendar={options.calendar}>
          <Probe instant="2023-04-27T20:00:00Z" />
        </PreferencesProvider>
      </IntlTestProvider>
    </QueryClientProvider>,
  );
}

describe("useFormatters", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("formats with the active locale, calendar and the tenant's time zone", async () => {
    renderProbe({ locale: "en", calendar: "BikramSambat" });
    await waitFor(() => expect(screen.getByTestId("date")).toHaveTextContent("15 Baisakh 2080"));
    expect(screen.getByTestId("money")).toHaveTextContent("NPR 1,250.00");
  });

  it("uses the session time zone (a UTC tenant sees the 27th, i.e. BS 14)", async () => {
    renderProbe({ locale: "en", calendar: "BikramSambat", timeZoneId: "UTC" });
    await waitFor(() => expect(screen.getByTestId("date")).toHaveTextContent("14 Baisakh 2080"));
  });

  it("falls back to Asia/Kathmandu before the session loads", () => {
    global.fetch = vi.fn().mockReturnValue(new Promise(() => {})) as unknown as typeof fetch;
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <IntlTestProvider>
          <PreferencesProvider calendar="Gregorian">
            <Probe instant="2023-04-27T20:00:00Z" />
          </PreferencesProvider>
        </IntlTestProvider>
      </QueryClientProvider>,
    );
    expect(screen.getByTestId("date")).toHaveTextContent("Apr 28, 2023");
  });
});
