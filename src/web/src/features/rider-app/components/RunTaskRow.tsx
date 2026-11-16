import { formatRs } from "@/features/cod/lib/codFormat";
import { SyncNote } from "@/features/rider-app/components/SyncNote";
import { taskResultLabel, type TaskSyncState } from "@/features/rider-app/lib/runModel";
import type { MyTodayTask } from "@/features/riders/types";
import { cn } from "@/lib/utils";

const ROW = "flex min-h-14 w-full items-center gap-3 rounded-xl border border-border bg-card px-4 py-3 text-left";

export function RunTaskRow({ task, syncState, onOpen }: { task: MyTodayTask; syncState: TaskSyncState; onOpen?: () => void }) {
  const right =
    task.status === "Pending" ? (task.codAmount > 0 ? formatRs(task.codAmount) : "Prepaid") : taskResultLabel(task);
  const body = (
    <>
      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-muted text-xs font-semibold" aria-hidden="true">
        {task.sequence}
      </span>
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-sm font-semibold">{task.recipientName}</span>
        <span className="truncate text-xs text-muted-foreground">{`${task.type} · ${task.recipientAddressLine}`}</span>
        <SyncNote state={syncState} />
      </span>
      <span className={cn("shrink-0 text-xs font-semibold", task.status === "Failed" && "text-destructive")}>{right}</span>
    </>
  );
  return onOpen ? (
    <button type="button" onClick={onOpen} className={cn(ROW, "hover:bg-accent")}>
      {body}
    </button>
  ) : (
    <div className={ROW}>{body}</div>
  );
}
