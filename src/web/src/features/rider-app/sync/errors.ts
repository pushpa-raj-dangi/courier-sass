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
  if (error.status === 401) return false;
  return error.status >= 400 && error.status < 500;
}
