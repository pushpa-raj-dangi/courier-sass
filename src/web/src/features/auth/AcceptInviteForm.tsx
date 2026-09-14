"use client";

import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldError } from "@/components/ui/field-error";
import { acceptInviteSchema, type AcceptInviteFormValues } from "@/features/auth/schema";

export function AcceptInviteForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const token = searchParams.get("token");
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<AcceptInviteFormValues>({ resolver: zodResolver(acceptInviteSchema) });

  async function onSubmit(values: AcceptInviteFormValues) {
    setServerError(null);
    let response: Response;
    try {
      response = await fetch("/api/auth/accept-invite", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token, password: values.password }),
      });
    } catch {
      // A public, one-shot, unauthenticated page — the invitee has no other way to tell a
      // network failure apart from a bad/expired link without this feedback.
      setServerError("Couldn't reach the server. Check your connection and try again.");
      return;
    }
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setServerError(body.error ?? "Something went wrong.");
      return;
    }
    router.push("/dashboard");
  }

  if (!token) {
    return (
      <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
        <CardContent className="pt-6">
          <FieldError id="invite-token-error">This invite link is missing its token. Check the link in your email and try again.</FieldError>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
      <CardHeader>
        <CardTitle asChild className="font-display text-xl">
          <h1>Set your password</h1>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              autoComplete="new-password"
              aria-invalid={!!errors.password}
              aria-describedby={errors.password ? "password-error" : undefined}
              {...register("password")}
            />
            {errors.password && (
              <FieldError id="password-error">{errors.password.message}</FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="confirmPassword">Confirm Password</Label>
            <Input
              id="confirmPassword"
              type="password"
              autoComplete="new-password"
              aria-invalid={!!errors.confirmPassword}
              aria-describedby={errors.confirmPassword ? "confirmPassword-error" : undefined}
              {...register("confirmPassword")}
            />
            {errors.confirmPassword && (
              <FieldError id="confirmPassword-error">{errors.confirmPassword.message}</FieldError>
            )}
          </div>
          {serverError && (
            <FieldError id="accept-invite-server-error">{serverError}</FieldError>
          )}
          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Setting password..." : "Set Password & Sign In"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
