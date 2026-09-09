"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Mail, Lock, Eye, EyeOff, ArrowRight, AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldError } from "@/components/ui/field-error";
import { platformLoginSchema, type PlatformLoginFormValues } from "@/features/platform-auth/schema";

export function PlatformLoginForm() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<PlatformLoginFormValues>({ resolver: zodResolver(platformLoginSchema) });

  async function onSubmit(values: PlatformLoginFormValues) {
    setServerError(null);
    const response = await fetch("/api/platform-auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setServerError(body.error ?? "Login failed.");
      return;
    }
    router.push("/platform/organizations");
  }

  return (
    <Card className="w-full max-w-md shadow-xl shadow-primary/5">
      <CardHeader className="space-y-1.5 pb-4">
        <CardTitle className="font-display text-2xl">Platform Administration</CardTitle>
        <CardDescription>Sign in to manage organizations across the platform.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5" noValidate>
          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <div className="relative">
              <Mail
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="email"
                type="email"
                autoComplete="email"
                className="h-10 pl-9"
                aria-invalid={!!errors.email}
                aria-describedby={errors.email ? "email-error" : undefined}
                {...register("email")}
              />
            </div>
            {errors.email && (
              <FieldError id="email-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.email.message}
              </FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <div className="relative">
              <Lock
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="password"
                type={showPassword ? "text" : "password"}
                autoComplete="current-password"
                className="h-10 pl-9 pr-10"
                aria-invalid={!!errors.password}
                aria-describedby={errors.password ? "password-error" : undefined}
                {...register("password")}
              />
              <button
                type="button"
                onClick={() => setShowPassword((v) => !v)}
                aria-label={showPassword ? "Hide password" : "Show password"}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded-md p-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
              >
                {showPassword ? (
                  <EyeOff className="h-4 w-4" strokeWidth={2} />
                ) : (
                  <Eye className="h-4 w-4" strokeWidth={2} />
                )}
              </button>
            </div>
            {errors.password && (
              <FieldError id="password-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.password.message}
              </FieldError>
            )}
          </div>
          {serverError && (
            <FieldError id="platform-login-server-error" className="flex items-center gap-1.5">
              <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
              {serverError}
            </FieldError>
          )}
          <Button type="submit" size="lg" className="group w-full" disabled={isSubmitting}>
            {isSubmitting ? "Signing in..." : "Sign in"}
            {!isSubmitting && (
              <ArrowRight
                className="h-4 w-4 transition-transform group-hover:translate-x-0.5"
                strokeWidth={2}
              />
            )}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
