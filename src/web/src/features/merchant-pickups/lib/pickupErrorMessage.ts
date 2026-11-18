// The pickup cancel endpoint answers 400 with { error: { code, message } }; clientFetch rethrows
// the JSON body as the Error message. The backend message is already human-readable.
export function pickupErrorMessage(error: unknown, fallback = "Couldn't cancel this pickup. Try again."): string {
  try {
    const message = JSON.parse((error as Error).message)?.error?.message;
    if (typeof message === "string") return message;
  } catch {
    // not a JSON body — fall through
  }
  return fallback;
}
