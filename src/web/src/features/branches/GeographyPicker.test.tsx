import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { GeographyPicker } from "./GeographyPicker";

function renderPicker(onChange = vi.fn(), source?: "staff" | "merchant") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    onChange,
    ...render(
      <QueryClientProvider client={queryClient}>
        <GeographyPicker
          value={{ provinceId: null, districtId: null, localLevelId: null, wardNumber: null }}
          onChange={onChange}
          source={source}
        />
      </QueryClientProvider>
    ),
  };
}

describe("GeographyPicker", () => {
  beforeEach(() => {
    global.fetch = vi.fn((url: string) => {
      if (url.includes("/provinces")) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: async () => [{ id: "p1", name: "Bagmati" }],
        });
      }
      return Promise.resolve({ ok: true, status: 200, json: async () => [] });
    }) as unknown as typeof fetch;
  });

  it("disables the District select until a Province is chosen", async () => {
    renderPicker();

    // Select's items only mount in the DOM while the dropdown is open (Radix renders SelectItem
    // via a Portal on open, not just because data loaded into a closed trigger) — wait on the
    // provinces fetch itself rather than asserting visible option text on a closed select.
    await waitFor(() =>
      expect(global.fetch).toHaveBeenCalledWith("/api/geography/provinces", undefined)
    );
    expect(screen.getByLabelText(/district/i)).toBeDisabled();
  });

  it("disables the Local Level select until a District is chosen", () => {
    renderPicker();

    expect(screen.getByLabelText(/local level/i)).toBeDisabled();
  });

  // Regression test for the Task 16 E2E-discovered bug: the merchant booking Sheet's
  // Recipient/Sender GeographyPicker instances must query the merchant-session BFF routes
  // (/api/merchant/geography/*), not the staff-only ones (/api/geography/*) — the merchant
  // session carries no staff cookie, so the staff routes always 401 for it, leaving the
  // Province/District/Local Level selects permanently empty.
  it("queries the merchant geography routes, not the staff ones, when source is merchant", async () => {
    renderPicker(vi.fn(), "merchant");

    await waitFor(() =>
      expect(global.fetch).toHaveBeenCalledWith("/api/merchant/geography/provinces", undefined)
    );
    expect(global.fetch).not.toHaveBeenCalledWith("/api/geography/provinces", undefined);
  });
});
