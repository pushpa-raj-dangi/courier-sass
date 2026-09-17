import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useLocationReporting } from "./useLocationReporting";

describe("useLocationReporting", () => {
  let watchPositionMock: ReturnType<typeof vi.fn>;
  let clearWatchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    watchPositionMock = vi.fn().mockReturnValue(1);
    clearWatchMock = vi.fn();
    Object.defineProperty(global.navigator, "geolocation", {
      value: { watchPosition: watchPositionMock, clearWatch: clearWatchMock },
      configurable: true,
    });
    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
    global.fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("starts watching position when mounted with the tab visible", () => {
    renderHook(() => useLocationReporting());

    expect(watchPositionMock).toHaveBeenCalledTimes(1);
  });

  it("stops watching when the tab becomes hidden and restarts when visible again", () => {
    renderHook(() => useLocationReporting());
    expect(watchPositionMock).toHaveBeenCalledTimes(1);

    Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
    document.dispatchEvent(new Event("visibilitychange"));
    expect(clearWatchMock).toHaveBeenCalledWith(1);

    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
    document.dispatchEvent(new Event("visibilitychange"));
    expect(watchPositionMock).toHaveBeenCalledTimes(2);
  });

  it("posts a ping to the BFF route when a position arrives", () => {
    renderHook(() => useLocationReporting());
    const onPosition = watchPositionMock.mock.calls[0][0] as (position: GeolocationPosition) => void;

    onPosition({
      coords: { latitude: 27.7, longitude: 85.3, accuracy: 10 } as GeolocationCoordinates,
      timestamp: Date.now(),
    } as GeolocationPosition);

    expect(global.fetch).toHaveBeenCalledWith(
      "/api/riders/me/location",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("stops watching on unmount", () => {
    const { unmount } = renderHook(() => useLocationReporting());
    unmount();

    expect(clearWatchMock).toHaveBeenCalledWith(1);
  });
});
