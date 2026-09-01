// CodEndpoints.HandleAsync (backend) wraps every CodValidationException as
// { error: { code: "COD_VALIDATION_FAILED", message: "<human-readable text>" } } — unlike
// Manifests' scan-rejection codes (manifestErrorMessages.ts), Cod's message is already
// human-readable, so this just extracts it directly rather than mapping a code to a label.
export function codErrorMessage(error: unknown): string {
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
