import { describe, expect, it, vi, beforeEach } from "vitest";
import { clientFetch, SessionExpiredError, TrialExpiredError } from "./clientFetch";

describe("clientFetch", () => {
  beforeEach(() => {
    global.fetch = vi.fn();
  });

  it("returns the parsed JSON body on success", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ hello: "world" }),
    });

    const result = await clientFetch<{ hello: string }>("/api/example");

    expect(result).toEqual({ hello: "world" });
    expect(global.fetch).toHaveBeenCalledWith("/api/example", undefined);
  });

  it("returns undefined for a 204 No Content response without parsing a body", async () => {
    // A 204 has no body to parse — calling .json() on it throws "Unexpected end of JSON
    // input", which must never happen here (this masked a real backend success as a
    // client-side failure for platform-settings' PUT/reset endpoints).
    const json = vi.fn().mockRejectedValue(new Error("Unexpected end of JSON input"));
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      status: 204,
      json,
    });

    const result = await clientFetch<void>("/api/example", { method: "POST" });

    expect(result).toBeUndefined();
    expect(json).not.toHaveBeenCalled();
  });

  it("throws SessionExpiredError on a 401", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => null,
    });

    await expect(clientFetch("/api/example")).rejects.toBeInstanceOf(SessionExpiredError);
  });

  it("throws TrialExpiredError on a 402", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 402,
      json: async () => null,
    });

    await expect(clientFetch("/api/example")).rejects.toBeInstanceOf(TrialExpiredError);
  });

  it("throws a plain Error on any other non-OK status when there is no JSON body", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => null,
    });

    await expect(clientFetch("/api/example")).rejects.toThrow("Request to /api/example failed with 500");
  });

  it("throws an Error carrying the JSON error body when the server sends one", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ error: { code: "MANIFEST_SCAN_REJECTED", reason: "SHIPMENT_NOT_FOUND" } }),
    });

    await expect(clientFetch("/api/example")).rejects.toThrow(
      JSON.stringify({ error: { code: "MANIFEST_SCAN_REJECTED", reason: "SHIPMENT_NOT_FOUND" } })
    );
  });
});
