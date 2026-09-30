import { afterEach, describe, expect, it, vi } from "vitest";
import { act, screen } from "@testing-library/react";
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

  describe("without a fixed now", () => {
    afterEach(() => {
      vi.useRealTimers();
    });

    it("moves to the next day after Kathmandu midnight", () => {
      // 2026-09-30 18:00Z = 23:45 Kathmandu (UTC+5:45); midnight there is 18:15Z.
      const before = new Date("2026-09-30T18:00:00Z");
      const after = new Date("2026-09-30T18:16:00Z");
      vi.useFakeTimers();
      vi.setSystemTime(before);
      renderWithIntl(<DualDate />);

      expect(screen.getByText(formatDualDate(before, "en").ad)).toBeInTheDocument();
      expect(formatDualDate(after, "en").ad).not.toBe(formatDualDate(before, "en").ad);

      act(() => {
        vi.advanceTimersByTime(16 * 60_000);
      });

      expect(screen.getByText(formatDualDate(after, "en").ad)).toBeInTheDocument();
      expect(screen.queryByText(formatDualDate(before, "en").ad)).not.toBeInTheDocument();
    });
  });
});
