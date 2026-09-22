"use client";

import { Badge } from "@/components/ui/badge";
import { DataTable, type ColumnDef, type PagedResult } from "@/components/data-table/DataTable";
import { useNotificationTemplates } from "@/features/admin/settings/notification-templates/hooks/useNotificationTemplates";
import { useNotificationTemplatePanelStore } from "@/features/admin/settings/notification-templates/store/notificationTemplatePanelStore";
import { NotificationTemplateEditSheet } from "@/features/admin/settings/notification-templates/components/NotificationTemplateEditSheet";
import type { NotificationChannel, NotificationLocale, NotificationTemplate } from "@/features/admin/settings/notification-templates/types";

const EVENT_TYPES = ["shipment.created", "shipment.delivered", "shipment.ndr_created", "cod.collected", "user.invited"];
// Restricted to Email: NotificationDispatchService only ever dispatches the Email channel today
// (Sms/WhatsApp/Push implementations exist but nothing invokes them), so surfacing those channels
// here would let an admin configure a "Custom" template that can never actually be sent. Add a
// channel back to this list only once NotificationDispatchService actually dispatches it.
const CHANNELS: NotificationChannel[] = ["Email"];

const LOCALES: { code: NotificationLocale; label: string }[] = [{ code: "en", label: "English" }, { code: "ne", label: "नेपाली" }];

interface Row {
  eventType: string;
  channel: NotificationChannel;
  locale: NotificationLocale;
  override: NotificationTemplate | null;
}

export function NotificationTemplatesPanel() {
  const { data, isLoading } = useNotificationTemplates();
  const openEdit = useNotificationTemplatePanelStore((s) => s.openEdit);

  const rows: Row[] = EVENT_TYPES.flatMap((eventType) =>
    CHANNELS.flatMap((channel) =>
      LOCALES.map(({ code }) => ({
        eventType,
        channel,
        locale: code,
        override: data?.find((t) => t.eventType === eventType && t.channel === channel && t.locale === code) ?? null,
      }))
    )
  );

  const columns: ColumnDef<Row>[] = [
    { key: "eventType", header: "Event", render: (r) => r.eventType },
    { key: "locale", header: "Language", render: (r) => LOCALES.find((l) => l.code === r.locale)!.label },
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
        <button type="button" className="text-sm font-medium text-foreground hover:underline" onClick={() => openEdit(r.eventType, r.channel, r.locale)}>
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
        rowKey={(r) => `${r.eventType}-${r.channel}-${r.locale}`}
      />
      <NotificationTemplateEditSheet />
    </div>
  );
}
