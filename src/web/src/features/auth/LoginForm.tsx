"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { ArrowRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { loginSchema, type LoginFormValues } from "@/features/auth/schema";
import { cn } from "@/lib/utils";
import {
  AuthFormHeader,
  Field,
  PasswordToggle,
  authButtonClass,
  authInputClass,
  usePasswordVisibility,
} from "@/features/auth/fields";

export function LoginForm() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const { shown: showPassword, toggle: togglePassword } = usePasswordVisibility();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) });

  async function onSubmit(values: LoginFormValues) {
    setServerError(null);
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setServerError(body.error ?? "Login failed.");
      return;
    }
    router.push("/dashboard");
  }

  return (
    <div className="w-full max-w-sm">
      <AuthFormHeader title="Sign in" description="Manage branches, shipments, and deliveries." />

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        <div className="space-y-3">
          <Field id="email" label="Email" error={errors.email?.message}>
            {(a11y) => (
              <Input
                {...a11y}
                type="email"
                autoComplete="email"
                className={authInputClass}
                {...register("email")}
              />
            )}
          </Field>
          <Field id="password" label="Password" error={errors.password?.message}>
            {(a11y) => (
              <div className="relative">
                <Input
                  {...a11y}
                  type={showPassword ? "text" : "password"}
                  autoComplete="current-password"
                  className={cn(authInputClass, "pr-10")}
                  {...register("password")}
                />
                <PasswordToggle shown={showPassword} onToggle={togglePassword} />
              </div>
            )}
          </Field>
        </div>

        <div className="space-y-3">
          {serverError &&<FieldError id="login-server-error">{serverError}</FieldError>}
          <Button type="submit" size="lg" className={authButtonClass} disabled={isSubmitting}>
            {isSubmitting ? (
              <>
                <Spinner className="h-4 w-4" />
                Signing in...
              </>
            ) : (
              <>
                Sign in
                <ArrowRight
                  className="h-4 w-4 transition-transform group-hover:translate-x-0.5"
                  strokeWidth={2}
                />
              </>
            )}
          </Button>
        </div>
      </form>
    </div>
  );
}
