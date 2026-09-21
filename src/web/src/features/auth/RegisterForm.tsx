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
import { registerSchema, type RegisterErrorKey, type RegisterFormValues } from "@/features/auth/schema";
import { cn } from "@/lib/utils";
import {
  AuthFormHeader,
  Field,
  PasswordToggle,
  authButtonClass,
  authInputClass as inputClass,
  usePasswordVisibility,
} from "@/features/auth/fields";

function slugify(value: string) {
  return value
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

export function RegisterForm() {
  const router = useRouter();
  const t = useTranslations("auth.register");
  const tv = useTranslations("auth.validation");
  const [serverError, setServerError] = useState<string | null>(null);
  const { shown: showPassword, toggle: togglePassword } = usePasswordVisibility();
  const [slugTouched, setSlugTouched] = useState(false);
  const {
    register,
    handleSubmit,
    setValue,
    getValues,
    formState: { errors, isSubmitting },
  } = useForm<RegisterFormValues>({ resolver: zodResolver(registerSchema) });

  // registerSchema messages are catalog keys; translate at display time.
  const fieldError = (message?: string) => (message ? tv(message as RegisterErrorKey) : undefined);

  function onOrganizationNameChange(event: React.ChangeEvent<HTMLInputElement>) {
    if (!slugTouched) {
      setValue("organizationSlug", slugify(event.target.value), { shouldValidate: true });
    }
  }

  function onSlugChange() {
    setSlugTouched(getValues("organizationSlug") !== slugify(getValues("organizationName") ?? ""));
  }

  async function onSubmit(values: RegisterFormValues) {
    setServerError(null);
    const response = await fetch("/api/auth/register", {
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
    <div className="w-full max-w-[28rem]">
      <AuthFormHeader
        title={t("title")}
        description={t("formDescription")}
      />

      <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
        <div className="grid gap-x-3 gap-y-3 sm:grid-cols-2">
          <Field
            id="organizationName"
            label={t("organizationName")}
            error={fieldError(errors.organizationName?.message)}
            className="sm:col-span-2"
          >
            {(a11y) => (
              <Input
                {...a11y}
                autoComplete="organization"
                className={inputClass}
                {...register("organizationName", { onChange: onOrganizationNameChange })}
              />
            )}
          </Field>
          <Field
            id="organizationSlug"
            label={t("organizationSlug")}
            hint={t("slugHint")}
            error={fieldError(errors.organizationSlug?.message)}
            className="sm:col-span-2"
          >
            {(a11y) => (
              <Input
                {...a11y}
                placeholder={t("slugPlaceholder")}
                autoCapitalize="none"
                spellCheck={false}
                className={cn(inputClass, "font-mono")}
                {...register("organizationSlug", { onChange: onSlugChange })}
              />
            )}
          </Field>

          <div className="border-t border-border sm:col-span-2" aria-hidden="true" />

          <Field id="ownerFullName" label={t("ownerFullName")} error={fieldError(errors.ownerFullName?.message)}>
            {(a11y) => (
              <Input {...a11y} autoComplete="name" className={inputClass} {...register("ownerFullName")} />
            )}
          </Field>
          <Field id="ownerEmail" label={t("email")} error={fieldError(errors.ownerEmail?.message)}>
            {(a11y) => (
              <Input
                {...a11y}
                type="email"
                autoComplete="email"
                className={inputClass}
                {...register("ownerEmail")}
              />
            )}
          </Field>
          <Field
            id="ownerPassword"
            label={t("password")}
            hint={t("passwordHint")}
            error={fieldError(errors.ownerPassword?.message)}
          >
            {(a11y) => (
              <div className="relative">
                <Input
                  {...a11y}
                  type={showPassword ? "text" : "password"}
                  autoComplete="new-password"
                  className={cn(inputClass, "pr-10")}
                  {...register("ownerPassword")}
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
          <Field id="confirmPassword" label={t("confirmPassword")} error={fieldError(errors.confirmPassword?.message)}>
            {(a11y) => (
              <Input
                {...a11y}
                type={showPassword ? "text" : "password"}
                autoComplete="new-password"
                className={inputClass}
                {...register("confirmPassword")}
              />
            )}
          </Field>
        </div>

        <div className="space-y-3">
          {serverError && <FieldError id="register-server-error">{serverError}</FieldError>}
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
          <p className="text-xs text-muted-foreground">
            {t("footnote")}
          </p>
        </div>
      </form>
    </div>
  );
}
