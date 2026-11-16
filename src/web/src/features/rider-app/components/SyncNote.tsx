import type { TaskSyncState } from "@/features/rider-app/lib/runModel";

/** Offline status of a task's queued action, in words. Nothing for a synced task. */
export function SyncNote({ state }: { state: TaskSyncState }) {
  if (state === "waiting") return <span className="block text-xs font-medium text-status-ndr">Saved · waiting to sync</span>;
  if (state === "needs-review") return <span className="block text-xs font-medium text-destructive">Needs review — see Profile</span>;
  return null;
}
