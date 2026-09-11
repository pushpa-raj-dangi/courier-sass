import { QueryCache, MutationCache, QueryClient } from "@tanstack/react-query";
import { SessionExpiredError, TrialExpiredError } from "@/lib/api/clientFetch";
import { useAuthStore } from "@/lib/store/authStore";

// /billing requires org.billing.manage, which only OrganizationOwner has — and this queryClient
// is a single module-level singleton shared across the (staff), /merchant, /rider, and /platform
// route groups (see comment below). Redirecting unconditionally bounces merchant-portal users
// (a different auth realm entirely), riders (mid-task, via a full page reload), and non-owner
// staff to a page they're permission-gated out of, so only redirect from staff surfaces.
export function redirectToBillingOnTrialExpired(error: unknown) {
  if (!(error instanceof TrialExpiredError) || typeof window === "undefined") return;
  const path = window.location.pathname;
  if (path === "/billing" || path.startsWith("/merchant") || path.startsWith("/rider") || path.startsWith("/platform")) {
    return;
  }
  window.location.href = "/billing";
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
