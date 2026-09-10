import { QueryCache, MutationCache, QueryClient } from "@tanstack/react-query";
import { SessionExpiredError, TrialExpiredError } from "@/lib/api/clientFetch";
import { useAuthStore } from "@/lib/store/authStore";

function redirectToBillingOnTrialExpired(error: unknown) {
  if (error instanceof TrialExpiredError && typeof window !== "undefined" && window.location.pathname !== "/billing") {
    window.location.href = "/billing";
  }
}

// Module-level singleton, shared by <Providers> (see src/app/providers.tsx) and by code that
// lives outside the React tree — most notably the rider outbox's syncManager.ts, which drains
// queued offline actions on a timer/online listener and has no component to call
// useQueryClient() from. Invalidating queries after a background drain (e.g. "riders/me/tasks/
// today") only reaches the live cache the UI is reading from if everyone shares this one
// instance — a second `new QueryClient()` elsewhere would invalidate a cache nobody renders from.
export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error) => {
      if (error instanceof SessionExpiredError) {
        useAuthStore.getState().showReauthDialog();
      }
      redirectToBillingOnTrialExpired(error);
    },
  }),
  mutationCache: new MutationCache({
    onError: redirectToBillingOnTrialExpired,
  }),
});
