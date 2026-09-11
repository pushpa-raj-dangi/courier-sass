import { describe, expect, it } from "vitest";
import { isNonRetryableError, QueuedRequestError } from "./errors";

describe("isNonRetryableError", () => {
  it("treats a 401 (expired session) as retryable, not terminal — the item should stay pending, not stuck", () => {
    expect(isNonRetryableError(new QueuedRequestError(401, "unauthorized"))).toBe(false);
  });

  it("treats a 402 (TRIAL_EXPIRED) as retryable, not terminal — clears once the org's owner picks a plan", () => {
    expect(isNonRetryableError(new QueuedRequestError(402, "trial expired"))).toBe(false);
  });

  it.each([400, 403, 404, 409, 422, 499])(
    "treats a %i as non-retryable/terminal",
    (status) => {
      expect(isNonRetryableError(new QueuedRequestError(status, "bad request"))).toBe(true);
    }
  );

  it("treats a 5xx as retryable (not a client-side rejection of the request)", () => {
    expect(isNonRetryableError(new QueuedRequestError(500, "server error"))).toBe(false);
  });

  it("treats a plain network failure (not a QueuedRequestError) as retryable", () => {
    expect(isNonRetryableError(new TypeError("Failed to fetch"))).toBe(false);
  });
});
