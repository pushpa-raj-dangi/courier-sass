"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
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
  const t = useTranslations("auth.login");
  const tv = useTranslations("auth.validation");
  const [serverError, setServerError] = useState<string | null>(null);
  const { shown: showPassword, toggle: togglePassword } = usePasswordVisibility();
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({ resolver: zodResolver(loginSchema) });

  // loginSchema messages are catalog keys; translate at display time.
  const fieldError = (message?: string) =>
    message ? tv(message as "emailInvalid" | "passwordRequired") : undefined;

  async function onSubmit(values: LoginFormValues) {
    setServerError(null);
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(values),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setServerError(body.error ?? t("failed"));
      return;
    }
    router.push("/dashboard");
  }

  return (
    <div className="w-full max-w-sm">
      <AuthFormHeader title={t("title")} description={t("description")} />

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        <div className="space-y-3">
          <Field id="email" label={t("email")} error={fieldError(errors.email?.message)}>
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
          <Field id="password" label={t("password")} error={fieldError(errors.password?.message)}>
            {(a11y) => (
              <div className="relative">
                <Input
                  {...a11y}
                  type={showPassword ? "text" : "password"}
                  autoComplete="current-password"
                  className={cn(authInputClass, "pr-10")}
                  {...register("password")}
                />
                <PasswordToggle
                  shown={showPassword}
                  onToggle={togglePassword}
                  showLabel={t("showPassword")}
                  hideLabel={t("hidePassword")}
                />
              </div>
            )}
          </Field>
        </div>

        <div className="space-y-3">
          {serverError && <FieldError id="login-server-error">{serverError}</FieldError>}
          <Button type="submit" size="lg" className={authButtonClass} disabled={isSubmitting}>
            {isSubmitting ? (
              <>
                <Spinner className="h-4 w-4" />
                {t("submitting")}
              </>
            ) : (
              <>
                {t("submit")}
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
