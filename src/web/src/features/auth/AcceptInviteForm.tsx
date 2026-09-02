"use client";

import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
          <p className="flex items-center gap-1.5 text-sm text-destructive">
            <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
            This invite link is missing its token. Check the link in your email and try again.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="w-full max-w-sm shadow-xl shadow-primary/5">
      <CardHeader>
        <CardTitle className="font-display text-xl">Set your password</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input id="password" type="password" autoComplete="new-password" {...register("password")} />
            {errors.password && (
              <p className="flex items-center gap-1.5 text-sm text-destructive">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.password.message}
              </p>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="confirmPassword">Confirm Password</Label>
            <Input id="confirmPassword" type="password" autoComplete="new-password" {...register("confirmPassword")} />
            {errors.confirmPassword && (
              <p className="flex items-center gap-1.5 text-sm text-destructive">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.confirmPassword.message}
              </p>
            )}
          </div>
          {serverError && (
            <p className="flex items-center gap-1.5 text-sm text-destructive">
              <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
              {serverError}
            </p>
          )}
          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Setting password..." : "Set Password & Sign In"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
