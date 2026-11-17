import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
vi.mock("next-themes", () => ({ useTheme: () => ({ resolvedTheme: "dark", setTheme: vi.fn() }) }));

import TrackPage from "./page";

describe("TrackPage", () => {
  it("renders one h1, the AWB field, the public note, and no marketing grid", () => {
    const { container } = render(<TrackPage />);
    const h1s = screen.getAllByRole("heading", { level: 1 });
    expect(h1s).toHaveLength(1);
    expect(h1s[0]).toHaveTextContent("Track a parcel");
    expect(screen.getByLabelText("AWB number")).toBeInTheDocument();
    expect(screen.getByText(/no account needed/i)).toBeInTheDocument();
    expect(container.querySelector('[data-testid="marketing-grid"]')).toBeNull();
  });
});
