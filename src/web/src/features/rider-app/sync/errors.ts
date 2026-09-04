export class QueuedRequestError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = "QueuedRequestError";
  }
}

export function isNonRetryableError(error: unknown): boolean {
  return error instanceof QueuedRequestError && error.status >= 400 && error.status < 500;
}
