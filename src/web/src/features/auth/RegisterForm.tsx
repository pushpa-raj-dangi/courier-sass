"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Building2, Mail, Lock, User, Eye, EyeOff, ArrowRight, AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldError } from "@/components/ui/field-error";
import { registerSchema, type RegisterFormValues } from "@/features/auth/schema";

function slugify(value: string) {
  return value
    .toLowerCase()
    .trim()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

export function RegisterForm() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const [showPassword, setShowPassword] = useState(false);
  const [slugTouched, setSlugTouched] = useState(false);
  const {
    register,
    handleSubmit,
    setValue,
    getValues,
    formState: { errors, isSubmitting },
  } = useForm<RegisterFormValues>({ resolver: zodResolver(registerSchema) });

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
      setServerError(body.error ?? "Registration failed.");
      return;
    }
    router.push("/dashboard");
  }

  return (
    <Card className="w-full max-w-md shadow-xl shadow-primary/5">
      <CardHeader className="space-y-1.5 pb-4">
        <CardTitle asChild className="font-display text-2xl">
          <h1>Create your organization</h1>
        </CardTitle>
        <CardDescription>Set up your courier operation on the platform.</CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5" noValidate>
          <div className="space-y-2">
            <Label htmlFor="organizationName">Organization name</Label>
            <div className="relative">
              <Building2
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="organizationName"
                autoComplete="organization"
                className="h-10 pl-9"
                aria-invalid={!!errors.organizationName}
                aria-describedby={errors.organizationName ? "organizationName-error" : undefined}
                {...register("organizationName", { onChange: onOrganizationNameChange })}
              />
            </div>
            {errors.organizationName && (
              <FieldError id="organizationName-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.organizationName.message}
              </FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="organizationSlug">Organization slug</Label>
            <Input
              id="organizationSlug"
              className="h-10"
              placeholder="e.g. kathmandu-express"
              aria-invalid={!!errors.organizationSlug}
              aria-describedby={errors.organizationSlug ? "organizationSlug-error" : undefined}
              {...register("organizationSlug", { onChange: onSlugChange })}
            />
            {errors.organizationSlug && (
              <FieldError id="organizationSlug-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.organizationSlug.message}
              </FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="ownerFullName">Your full name</Label>
            <div className="relative">
              <User
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="ownerFullName"
                autoComplete="name"
                className="h-10 pl-9"
                aria-invalid={!!errors.ownerFullName}
                aria-describedby={errors.ownerFullName ? "ownerFullName-error" : undefined}
                {...register("ownerFullName")}
              />
            </div>
            {errors.ownerFullName && (
              <FieldError id="ownerFullName-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.ownerFullName.message}
              </FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="ownerEmail">Email</Label>
            <div className="relative">
              <Mail
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="ownerEmail"
                type="email"
                autoComplete="email"
                className="h-10 pl-9"
                aria-invalid={!!errors.ownerEmail}
                aria-describedby={errors.ownerEmail ? "ownerEmail-error" : undefined}
                {...register("ownerEmail")}
              />
            </div>
            {errors.ownerEmail && (
              <FieldError id="ownerEmail-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.ownerEmail.message}
              </FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="ownerPassword">Password</Label>
            <div className="relative">
              <Lock
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="ownerPassword"
                type={showPassword ? "text" : "password"}
                autoComplete="new-password"
                className="h-10 pl-9 pr-10"
                aria-invalid={!!errors.ownerPassword}
                aria-describedby={errors.ownerPassword ? "ownerPassword-error" : undefined}
                {...register("ownerPassword")}
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
            {errors.ownerPassword && (
              <FieldError id="ownerPassword-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.ownerPassword.message}
              </FieldError>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="confirmPassword">Confirm password</Label>
            <div className="relative">
              <Lock
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="confirmPassword"
                type={showPassword ? "text" : "password"}
                autoComplete="new-password"
                className="h-10 pl-9"
                aria-invalid={!!errors.confirmPassword}
                aria-describedby={errors.confirmPassword ? "confirmPassword-error" : undefined}
                {...register("confirmPassword")}
              />
            </div>
            {errors.confirmPassword && (
              <FieldError id="confirmPassword-error" className="flex items-center gap-1.5">
                <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
                {errors.confirmPassword.message}
              </FieldError>
            )}
          </div>
          {serverError && (
            <FieldError id="register-server-error" className="flex items-center gap-1.5">
              <AlertCircle className="h-3.5 w-3.5" strokeWidth={2} />
              {serverError}
            </FieldError>
          )}
          <Button type="submit" size="lg" className="group w-full" disabled={isSubmitting}>
            {isSubmitting ? "Creating organization..." : "Create organization"}
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
