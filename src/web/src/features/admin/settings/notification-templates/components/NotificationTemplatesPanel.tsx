"use client";

import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { useNotificationTemplates } from "@/features/admin/settings/notification-templates/hooks/useNotificationTemplates";
import { useNotificationTemplatePanelStore } from "@/features/admin/settings/notification-templates/store/notificationTemplatePanelStore";
import { NotificationTemplateEditSheet } from "@/features/admin/settings/notification-templates/components/NotificationTemplateEditSheet";
import type { NotificationChannel, NotificationTemplate } from "@/features/admin/settings/notification-templates/types";

const EVENT_TYPES = ["shipment.created", "shipment.delivered", "shipment.ndr_created", "cod.collected", "user.invited"];
// Restricted to Email: NotificationDispatchService only ever dispatches the Email channel today
// (Sms/WhatsApp/Push implementations exist but nothing invokes them), so surfacing those channels
// here would let an admin configure a "Custom" template that can never actually be sent. Add a
// channel back to this list only once NotificationDispatchService actually dispatches it.
const CHANNELS: NotificationChannel[] = ["Email"];

interface Row {
  eventType: string;
  channel: NotificationChannel;
  override: NotificationTemplate | null;
}

export function NotificationTemplatesPanel() {
  const { data, isLoading } = useNotificationTemplates();
  const openEdit = useNotificationTemplatePanelStore((s) => s.openEdit);

  const rows: Row[] = EVENT_TYPES.flatMap((eventType) =>
    CHANNELS.map((channel) => ({
      eventType,
      channel,
      override: data?.find((t) => t.eventType === eventType && t.channel === channel) ?? null,
    }))
  );

  const columns: ColumnDef<Row>[] = [
    { key: "eventType", header: "Event", render: (r) => r.eventType },
    {
      key: "status",
      header: "Status",
      render: (r) =>
        r.override ? (
          <Badge variant={r.override.isActive ? "default" : "secondary"}>{r.override.isActive ? "Custom" : "Custom (inactive)"}</Badge>
        ) : (
          <Badge variant="outline">Using default</Badge>
        ),
    },
    { key: "body", header: "Body", render: (r) => (r.override ? r.override.body : "—") },
    {
      key: "actions",
      header: "",
      render: (r) => (
        <button type="button" className="text-sm font-medium text-foreground hover:underline" onClick={() => openEdit(r.eventType, r.channel)}>
          Edit
        </button>
      ),
    },
  ];

  const pagedRows: PagedResult<Row> = { items: rows, page: 1, pageSize: rows.length, totalCount: rows.length, totalPages: 1 };

  return (
    <div className="space-y-4">
      <DataTable
        columns={columns}
        data={pagedRows}
        isLoading={isLoading}
        emptyTitle="No notification event types configured"
        onPageChange={() => {}}
        rowKey={(r) => `${r.eventType}-${r.channel}`}
      />
      <NotificationTemplateEditSheet />
    </div>
  );
}
