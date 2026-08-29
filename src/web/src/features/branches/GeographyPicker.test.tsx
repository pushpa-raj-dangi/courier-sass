import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { GeographyPicker } from "./GeographyPicker";

function renderPicker(onChange = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    onChange,
    ...render(
      <QueryClientProvider client={queryClient}>
        <GeographyPicker
          value={{ provinceId: null, districtId: null, localLevelId: null, wardNumber: null }}
          onChange={onChange}
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
});
