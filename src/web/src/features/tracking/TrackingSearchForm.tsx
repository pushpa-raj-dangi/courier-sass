"use client";

import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { ArrowRight, PackageSearch } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FieldError } from "@/components/ui/field-error";
import { trackingSearchSchema, type TrackingSearchFormValues } from "@/features/tracking/schema";

export function TrackingSearchForm() {
  const router = useRouter();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<TrackingSearchFormValues>({ resolver: zodResolver(trackingSearchSchema) });

  function onSubmit(values: TrackingSearchFormValues) {
    router.push(`/track/${encodeURIComponent(values.awb.trim())}`);
  }

  return (
    <div className="relative w-full max-w-sm overflow-hidden rounded-md border border-marketing-border bg-marketing-bg-elevated p-6 shadow-[8px_8px_0_0_hsl(var(--marketing-accent)/0.18)]">
      <span
        aria-hidden="true"
        className="absolute inset-x-6 top-0 h-px bg-gradient-to-r from-transparent via-marketing-accent/70 to-transparent"
      />
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="awb" className="text-marketing-fg">
            AWB Number
          </Label>
          <div className="relative">
            <PackageSearch
              className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-marketing-muted-fg"
              strokeWidth={2}
              aria-hidden="true"
            />
            <Input
              id="awb"
              type="text"
              autoComplete="off"
              placeholder="NP2026-000123"
              className="border-marketing-border bg-transparent pl-9 font-mono text-marketing-fg placeholder:text-marketing-muted-fg/60 focus-visible:ring-marketing-accent"
              aria-invalid={!!errors.awb}
              aria-describedby={errors.awb ? "awb-error" : undefined}
              {...register("awb")}
            />
          </div>
          {errors.awb && (
            <FieldError id="awb-error">{errors.awb.message}</FieldError>
          )}
        </div>
        <button
          type="submit"
          disabled={isSubmitting}
          className="group flex h-11 w-full items-center justify-center gap-2 rounded-md bg-marketing-accent px-6 text-sm font-medium text-marketing-accent-fg shadow-[4px_4px_0_0_hsl(var(--marketing-fg)/0.18)] transition-opacity hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-marketing-accent focus-visible:ring-offset-2 focus-visible:ring-offset-marketing-bg disabled:pointer-events-none disabled:opacity-50"
        >
          Track
          <ArrowRight
            className="h-4 w-4 transition-transform group-hover:translate-x-0.5"
            strokeWidth={2}
            aria-hidden="true"
          />
        </button>
      </form>
    </div>
  );
}
