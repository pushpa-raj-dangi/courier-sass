import { SessionExpiredError, isNotFoundError } from "@/lib/api/clientFetch";
import { manifestScanErrorMessage } from "@/features/manifests/lib/manifestErrorMessages";

export const NOT_FOUND_MESSAGE = "AWB not found in your branches.";
export const NETWORK_MESSAGE = "Network error — not recorded, scan again.";
const GENERIC_MESSAGE = "Couldn't record this scan. Check the AWB and try again.";

export function scanErrorMessage(error: unknown): string {
  if (error instanceof SessionExpiredError) return "Your session expired — sign in again.";
  if (error instanceof TypeError) return NETWORK_MESSAGE;
  if (isNotFoundError(error)) return NOT_FOUND_MESSAGE;
  const message = error instanceof Error ? error.message : "";
  try {
    const parsed = JSON.parse(message);
    const reason = parsed?.error?.reason;
    if (typeof reason === "string") return manifestScanErrorMessage(reason);
    const serverMessage = parsed?.error?.message;
    if (typeof serverMessage === "string" && serverMessage) return serverMessage;
  } catch {
    // not JSON
  }
  return GENERIC_MESSAGE;
}
