"use client";

import { useEffect, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useNotificationTemplates } from "@/features/admin/settings/notification-templates/hooks/useNotificationTemplates";
import { useUpsertNotificationTemplate } from "@/features/admin/settings/notification-templates/hooks/useUpsertNotificationTemplate";
import { useNotificationTemplatePanelStore } from "@/features/admin/settings/notification-templates/store/notificationTemplatePanelStore";
import { notificationTemplateSchema, type NotificationTemplateFormValues } from "@/features/admin/settings/notification-templates/schema";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function NotificationTemplateEditSheet() {
  const panel = useNotificationTemplatePanelStore((s) => s.panel);
  const close = useNotificationTemplatePanelStore((s) => s.close);
  const isOpen = panel.mode === "edit";
  const { data: templates } = useNotificationTemplates();
  const upsert = useUpsertNotificationTemplate();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, control, reset, formState: { errors, isSubmitting } } = useForm<NotificationTemplateFormValues>({
    resolver: zodResolver(notificationTemplateSchema),
    defaultValues: { body: "", isActive: true },
  });

  useEffect(() => {
    if (panel.mode === "edit") {
      const existing = templates?.find((t) => t.eventType === panel.eventType && t.channel === panel.channel && t.locale === panel.locale);
      reset({ body: existing?.body ?? "", isActive: existing?.isActive ?? true });
    }
  }, [panel, templates, reset]);

  async function onSubmit(values: NotificationTemplateFormValues) {
    if (panel.mode !== "edit") return;
    setServerError(null);
    try {
      await upsert.mutateAsync({ eventType: panel.eventType, channel: panel.channel, locale: panel.locale, body: values.body, isActive: values.isActive });
      close();
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{panel.mode === "edit" ? `${panel.eventType} — ${panel.channel} — ${panel.locale === "ne" ? "नेपाली" : "English"}` : "Edit Template"}</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="body">Body</Label>
            <Textarea id="body" rows={5} aria-invalid={!!errors.body} {...register("body")} />
            {errors.body && <FieldError id="body-error">{errors.body.message}</FieldError>}
          </div>
          <Controller
            name="isActive"
            control={control}
            render={({ field }) => (
              <div className="flex items-center gap-2">
                <Checkbox id="isActive" checked={field.value} onCheckedChange={field.onChange} />
                <Label htmlFor="isActive" className="font-normal">Active</Label>
              </div>
            )}
          />
          {serverError && <FieldError id="template-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Template"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
