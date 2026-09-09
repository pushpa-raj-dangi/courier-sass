import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

const readImpersonationMeta = vi.fn();
vi.mock("@/lib/impersonation", () => ({
  readImpersonationMeta: () => readImpersonationMeta(),
}));

const fetchMock = vi.fn();

import { ImpersonationBanner } from "./ImpersonationBanner";

describe("ImpersonationBanner", () => {
  beforeEach(() => {
    push.mockClear();
    readImpersonationMeta.mockReset();
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ ok: true }) });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("renders nothing when there is no impersonation session", () => {
    readImpersonationMeta.mockReturnValue(null);
    const { container } = render(<ImpersonationBanner />);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders the name, org, countdown and an End session button when impersonating", () => {
    readImpersonationMeta.mockReturnValue({
      name: "Jane Owner",
      organizationName: "Acme Courier",
      expiresAt: new Date(Date.now() + 90000).toISOString(),
    });
    render(<ImpersonationBanner />);

    expect(screen.getByText(/Impersonating Jane Owner at Acme Courier/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /end session/i })).toBeInTheDocument();
  });

  it("clicking End session calls the end-impersonation route and navigates away", async () => {
    readImpersonationMeta.mockReturnValue({
      name: "Jane Owner",
      organizationName: "Acme Courier",
      expiresAt: new Date(Date.now() + 90000).toISOString(),
    });
    render(<ImpersonationBanner />);

    fireEvent.click(screen.getByRole("button", { name: /end session/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith("/api/impersonation/end", { method: "POST" }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/platform/organizations"));
  });
});
