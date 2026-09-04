"use client";

import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle, ArrowRight, PackageSearch } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
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
    <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
      <CardHeader>
        <CardTitle className="font-display text-xl">Track your shipment</CardTitle>
        <CardDescription>Enter your AWB number to see its current status.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="awb">AWB Number</Label>
            <div className="relative">
              <PackageSearch
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="awb"
                type="text"
                autoComplete="off"
                className="pl-9"
                aria-invalid={!!errors.awb}
                aria-describedby={errors.awb ? "awb-error" : undefined}
                {...register("awb")}
              />
            </div>
            {errors.awb && (
              <FieldError id="awb-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.awb.message}
              </FieldError>
            )}
          </div>
          <Button type="submit" className="group w-full" disabled={isSubmitting}>
            Track
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" strokeWidth={2} />
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
