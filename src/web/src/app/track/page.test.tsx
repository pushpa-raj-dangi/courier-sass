import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("next-themes", () => ({ useTheme: () => ({ resolvedTheme: "dark", setTheme: vi.fn() }) }));

import TrackPage from "./page";

describe("TrackPage", () => {
  it("renders one h1, the public-tracking eyebrow, and the decorative grid", () => {
    const { container } = render(<TrackPage />);
    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByText(/^public tracking$/i)).toBeInTheDocument();
    expect(container.querySelector('[data-testid="marketing-grid"]')).not.toBeNull();
  });
});
