export class QueuedRequestError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = "QueuedRequestError";
  }
}

export function isNonRetryableError(error: unknown): boolean {
  if (!(error instanceof QueuedRequestError)) return false;
  // 401 means the rider's session expired — very plausible after being offline for a while.
  // That's not a permanent rejection of the request itself, so treat it like a network failure:
  // leave the item "pending" so it's retried once the session is refreshed, rather than marking
  // it "stuck" forever (which would silently lose a COD collection, see CLAUDE.md §39).
  // 402 means TRIAL_EXPIRED (TrialEnforcementMiddleware) — same recoverable shape as 401: the
  // org's owner picks a plan and it starts working again, so a queued delivery-completion or COD
  // collection should keep retrying rather than getting stuck and blocking every later item.
  if (error.status === 401 || error.status === 402) return false;
  return error.status >= 400 && error.status < 500;
}
