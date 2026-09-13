"use client";

import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { Skeleton } from "@/components/ui/skeleton";
import { Separator } from "@/components/ui/separator";
import { useOrganizationSettings } from "@/features/admin/settings/hooks/useOrganizationSettings";
import { useUpdateSecurityPolicy } from "@/features/admin/settings/hooks/useUpdateSecurityPolicy";
import { useRevokeAllSessions } from "@/features/admin/settings/hooks/useRevokeAllSessions";
import { securityPolicySchema, type SecurityPolicyFormValues } from "@/features/admin/settings/schema";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";

export function SecurityPolicyForm() {
  const { data, isLoading } = useOrganizationSettings();
  const updateSecurity = useUpdateSecurityPolicy();
  const revokeAllSessions = useRevokeAllSessions();
  const [serverError, setServerError] = useState<string | null>(null);
  const [revokeResult, setRevokeResult] = useState<"success" | "error" | null>(null);

  const { register, handleSubmit, reset, formState: { errors, isSubmitting } } = useForm<SecurityPolicyFormValues>({
    resolver: zodResolver(securityPolicySchema),
  });

  useEffect(() => {
    if (data) {
      reset({ refreshTokenLifetimeDays: data.security.refreshTokenLifetimeDays, minPasswordLength: data.security.minPasswordLength });
    }
  }, [data, reset]);

  async function onSubmit(values: SecurityPolicyFormValues) {
    setServerError(null);
    try {
      await updateSecurity.mutateAsync(values);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  async function handleRevokeAllSessions() {
    setRevokeResult(null);
    try {
      await revokeAllSessions.mutateAsync();
      setRevokeResult("success");
    } catch {
      setRevokeResult("error");
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-3" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-10 w-full" />
      </div>
    );
  }

  return (
    <div className="max-w-xl space-y-6">
      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        <div className="space-y-2">
          <Label htmlFor="refreshTokenLifetimeDays">Session Length (days)</Label>
          <Input id="refreshTokenLifetimeDays" type="number" min={1} max={30} aria-invalid={!!errors.refreshTokenLifetimeDays} {...register("refreshTokenLifetimeDays")} />
          {errors.refreshTokenLifetimeDays && <FieldError id="refreshTokenLifetimeDays-error">{errors.refreshTokenLifetimeDays.message}</FieldError>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="minPasswordLength">Minimum Password Length</Label>
          <Input id="minPasswordLength" type="number" min={8} max={64} aria-invalid={!!errors.minPasswordLength} {...register("minPasswordLength")} />
          {errors.minPasswordLength && <FieldError id="minPasswordLength-error">{errors.minPasswordLength.message}</FieldError>}
        </div>
        {serverError && <FieldError id="security-server-error">{serverError}</FieldError>}
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? <Spinner className="h-4 w-4" /> : "Save Security Policy"}
        </Button>
      </form>
      <Separator />
      <div className="space-y-2">
        <Label>Revoke All Sessions</Label>
        <p className="text-sm text-muted-foreground">Immediately signs out every staff member in your organization. They will need to log in again.</p>
        <Button type="button" variant="destructive" onClick={handleRevokeAllSessions} disabled={revokeAllSessions.isPending}>
          {revokeAllSessions.isPending ? <Spinner className="h-4 w-4" /> : "Revoke All Sessions"}
        </Button>
        {revokeResult === "success" && <p className="text-sm text-status-delivered">All sessions revoked.</p>}
        {revokeResult === "error" && <FieldError id="revoke-sessions-error">Couldn't revoke sessions. Try again.</FieldError>}
      </div>
    </div>
  );
}
