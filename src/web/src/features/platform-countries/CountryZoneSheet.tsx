"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { countryZoneSchema, type CountryZoneFormValues } from "@/features/platform-countries/schema";
import { usePlatformCountryZones } from "@/features/platform-countries/hooks/usePlatformCountryZones";
import { useCreateCountryZone } from "@/features/platform-countries/hooks/useCreateCountryZone";
import { useUpdateCountryZone } from "@/features/platform-countries/hooks/useUpdateCountryZone";
import { useCountryZonePanelStore } from "@/features/platform-countries/store/countryZonePanelStore";

export function CountryZoneSheet() {
  const panel = useCountryZonePanelStore((s) => s.panel);
  const close = useCountryZonePanelStore((s) => s.close);
  const isOpen = panel.mode !== "closed";
  const { data: zones } = usePlatformCountryZones();
  const create = useCreateCountryZone();
  const update = useUpdateCountryZone();
  const [serverError, setServerError] = useState<string | null>(null);

  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<CountryZoneFormValues>({
    resolver: zodResolver(countryZoneSchema),
    defaultValues: { name: "", description: "" },
  });

  useEffect(() => {
    if (panel.mode === "edit") {
      const existing = zones?.find((z) => z.id === panel.zoneId);
      reset({ name: existing?.name ?? "", description: existing?.description ?? "" });
    } else if (panel.mode === "create") {
      reset({ name: "", description: "" });
    }
  }, [panel, zones, reset]);

  async function onSubmit(values: CountryZoneFormValues) {
    setServerError(null);
    try {
      // JSON.stringify silently drops `undefined` keys, so relying on the raw form value here
      // would mean clearing the description field sends a body with no `description` key at all —
      // the backend's UpdateCountryZoneRequest.Description would then bind to whatever it defaults
      // to instead of reliably clearing the stored value. Coerce a blank field to an explicit
      // `null` so the intent to clear is always sent on the wire.
      const payload = { name: values.name, description: values.description?.trim() || null };
      if (panel.mode === "edit") {
        await update.mutateAsync({ id: panel.zoneId, ...payload });
      } else {
        await create.mutateAsync(payload);
      }
      close();
    } catch {
      setServerError("Couldn't save the country zone. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>{panel.mode === "edit" ? "Edit Country Zone" : "New Country Zone"}</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="zone-name">Name</Label>
            <Input id="zone-name" aria-invalid={!!errors.name} aria-describedby={errors.name ? "zone-name-error" : undefined} {...register("name")} />
            {errors.name && <FieldError id="zone-name-error">{errors.name.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="zone-description">Description</Label>
            <Textarea id="zone-description" rows={3} {...register("description")} />
          </div>
          {serverError && <FieldError id="zone-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Zone"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
