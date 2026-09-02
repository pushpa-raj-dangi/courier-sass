export function adminErrorMessage(error: unknown): string {
  try {
    const parsed = JSON.parse((error as Error).message);
    const message = parsed?.error?.message;
    if (typeof message === "string") {
      return message;
    }
  } catch {
    // fall through to generic message
  }
  return "Something went wrong. Check the fields and try again.";
}
