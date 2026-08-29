import { describe, expect, it, vi, beforeEach } from "vitest";
import { clientFetch, SessionExpiredError } from "./clientFetch";

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

  it("throws SessionExpiredError on a 401", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => null,
    });

    await expect(clientFetch("/api/example")).rejects.toBeInstanceOf(SessionExpiredError);
  });

  it("throws a plain Error on any other non-OK status", async () => {
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => null,
    });

    await expect(clientFetch("/api/example")).rejects.toThrow("Request to /api/example failed with 500");
  });
});
