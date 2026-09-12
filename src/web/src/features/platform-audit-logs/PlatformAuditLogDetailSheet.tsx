"use client";

import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { usePlatformAuditLogPanelStore } from "@/features/platform-audit-logs/store/platformAuditLogPanelStore";

export function PlatformAuditLogDetailSheet() {
  const panel = usePlatformAuditLogPanelStore((s) => s.panel);
  const close = usePlatformAuditLogPanelStore((s) => s.close);
  const record = panel.mode === "detail" ? panel.record : null;

  return (
    <Sheet open={panel.mode === "detail"} onOpenChange={(open) => !open && close()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{record?.action ?? "Audit event"}</SheetTitle>
        </SheetHeader>
        {record && (
          <div className="space-y-4 p-4 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{record.entityType}</Badge>
              <span className="text-muted-foreground">{record.entityId}</span>
              {record.isCrossTenantAction && <Badge variant="outline">Platform action</Badge>}
            </div>
            <dl className="space-y-2">
              <div>
                <dt className="text-muted-foreground">Organization</dt>
                <dd>{record.organizationName ?? "—"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Timestamp</dt>
                <dd>{new Date(record.timestamp).toLocaleString()}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Actor</dt>
                <dd>{record.userId ?? "System"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">IP address</dt>
                <dd>{record.ipAddress ?? "—"}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground">User agent</dt>
                <dd className="break-all">{record.userAgent ?? "—"}</dd>
              </div>
            </dl>
            {record.before && (
              <div>
                <dt className="mb-1 text-muted-foreground">Before</dt>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{record.before}</pre>
              </div>
            )}
            {record.after && (
              <div>
                <dt className="mb-1 text-muted-foreground">After</dt>
                <pre className="overflow-x-auto rounded-md bg-muted p-2 text-xs">{record.after}</pre>
              </div>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
