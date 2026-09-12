"use client";

import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { usePlatformJobPanelStore } from "@/features/platform-jobs/store/platformJobPanelStore";

export function PlatformJobDetailSheet() {
  const panel = usePlatformJobPanelStore((s) => s.panel);
  const close = usePlatformJobPanelStore((s) => s.close);
  const job = panel.mode === "detail" ? panel.job : null;

  return (
    <Sheet open={panel.mode === "detail"} onOpenChange={(open) => !open && close()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{job?.jobName ?? "Job"}</SheetTitle>
        </SheetHeader>
        {job && (
          <div className="space-y-4 p-4 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={job.state === "Failed" ? "destructive" : "secondary"}>{job.state}</Badge>
              <span className="text-muted-foreground">{job.jobId}</span>
            </div>
            <dl className="space-y-2">
              <div>
                <dt className="text-muted-foreground">Time</dt>
                <dd>{new Date(job.at).toLocaleString()}</dd>
              </div>
            </dl>
            {job.exceptionMessage && (
              <div>
                <span className="mb-1 text-muted-foreground">Exception</span>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{job.exceptionMessage}</pre>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
