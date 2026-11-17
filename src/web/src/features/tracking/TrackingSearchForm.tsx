"use client";

import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FieldError } from "@/components/ui/field-error";
import { cn } from "@/lib/utils";
import { trackingSearchSchema, type TrackingSearchFormValues } from "@/features/tracking/schema";

export function TrackingSearchForm({ defaultAwb }: { defaultAwb?: string }) {
  const router = useRouter();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<TrackingSearchFormValues>({
    resolver: zodResolver(trackingSearchSchema),
    defaultValues: { awb: defaultAwb ?? "" },
  });

  function onSubmit(values: TrackingSearchFormValues) {
    router.push(`/track/${encodeURIComponent(values.awb.trim())}`);
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="w-full space-y-2" noValidate>
      <div
        data-invalid={errors.awb ? "true" : undefined}
        className={cn(
          "flex h-12 items-center gap-2 rounded-lg border bg-card pl-3 pr-1 focus-within:ring-1",
          errors.awb ? "border-destructive focus-within:ring-destructive" : "border-input focus-within:ring-ring"
        )}
      >
        <Search className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <Input
          type="text"
          autoComplete="off"
          placeholder="NP2026-000123"
          aria-label="AWB number"
          className="h-full flex-1 border-0 bg-transparent px-0 font-mono shadow-none focus-visible:ring-0"
          aria-invalid={!!errors.awb}
          aria-describedby={errors.awb ? "awb-error" : undefined}
          {...register("awb")}
        />
        <Button type="submit" disabled={isSubmitting} className="h-11 shrink-0 px-4">
          Track
        </Button>
      </div>
      {errors.awb && <FieldError id="awb-error">{errors.awb.message}</FieldError>}
    </form>
  );
}
