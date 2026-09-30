import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithIntl } from "@/test/renderWithIntl";
import { DualDate } from "./DualDate";
import { formatDualDate } from "./formatDualDate";

describe("DualDate", () => {
  it("renders AD and BS lines with the BS suffix", () => {
    const now = new Date("2026-09-30T06:00:00Z");
    const { ad, bs } = formatDualDate(now, "en");
    renderWithIntl(<DualDate now={now} />);

    expect(screen.getByText(ad)).toBeInTheDocument();
    expect(screen.getByText(`${bs} BS`)).toBeInTheDocument();
  });

  it("renders the Nepali suffix in the ne locale", () => {
    const now = new Date("2026-09-30T06:00:00Z");
    const { bs } = formatDualDate(now, "ne");
    renderWithIntl(<DualDate now={now} />, { locale: "ne" });

    expect(screen.getByText(`${bs} वि.सं.`)).toBeInTheDocument();
  });

  it("renders only the AD line when BS is out of range", () => {
    const now = new Date("2050-01-01T00:00:00Z");
    renderWithIntl(<DualDate now={now} />);
    expect(screen.queryByText(/BS$/)).not.toBeInTheDocument();
  });
});
