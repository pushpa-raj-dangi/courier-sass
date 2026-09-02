"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { createManifestSchema, type CreateManifestFormValues } from "@/features/manifests/schema";
import { useCreateManifest } from "@/features/manifests/hooks/useCreateManifest";
import { useManifestPanelStore } from "@/features/manifests/store/manifestPanelStore";

export function ManifestCreateSheet() {
  const panel = useManifestPanelStore((s) => s.panel);
  const close = useManifestPanelStore((s) => s.close);
  const openDetail = useManifestPanelStore((s) => s.openDetail);
  const isOpen = panel.mode === "create";

  const createManifest = useCreateManifest();
  const { data: branches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateManifestFormValues>({
    resolver: zodResolver(createManifestSchema),
    defaultValues: { purpose: "Forward" },
  });

  async function onSubmit(values: CreateManifestFormValues) {
    setServerError(null);
    try {
      const created = await createManifest.mutateAsync(values);
      reset();
      openDetail(created.id);
    } catch {
      setServerError("Couldn't create the manifest. Check the fields and try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>New Manifest</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="fromBranchId">From Branch</Label>
            <Controller
              name="fromBranchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger
                    id="fromBranchId"
                    aria-label="From branch"
                    aria-invalid={!!errors.fromBranchId}
                    aria-describedby={errors.fromBranchId ? "fromBranchId-error" : undefined}
                  >
                    <SelectValue placeholder="Select origin branch" />
                  </SelectTrigger>
                  <SelectContent>
                    {branches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.fromBranchId && <FieldError id="fromBranchId-error">{errors.fromBranchId.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="toBranchId">To Branch</Label>
            <Controller
              name="toBranchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger
                    id="toBranchId"
                    aria-label="To branch"
                    aria-invalid={!!errors.toBranchId}
                    aria-describedby={errors.toBranchId ? "toBranchId-error" : undefined}
                  >
                    <SelectValue placeholder="Select destination branch" />
                  </SelectTrigger>
                  <SelectContent>
                    {branches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.toBranchId && <FieldError id="toBranchId-error">{errors.toBranchId.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="purpose">Purpose</Label>
            <Controller
              name="purpose"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="purpose" aria-label="Purpose">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Forward">Forward</SelectItem>
                    <SelectItem value="Return">Return</SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          {serverError && <FieldError id="manifest-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Manifest"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
