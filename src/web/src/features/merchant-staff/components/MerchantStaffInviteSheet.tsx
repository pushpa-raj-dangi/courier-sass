"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useInviteMerchantStaff } from "@/features/merchant-staff/hooks/useInviteMerchantStaff";
import { useMerchantStaffPanelStore } from "@/features/merchant-staff/store/merchantStaffPanelStore";
import { inviteMerchantStaffSchema, type InviteMerchantStaffFormValues } from "@/features/merchant-staff/schema";

export function MerchantStaffInviteSheet() {
  const panel = useMerchantStaffPanelStore((s) => s.panel);
  const close = useMerchantStaffPanelStore((s) => s.close);
  const isOpen = panel.mode === "create";

  const inviteStaff = useInviteMerchantStaff();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<InviteMerchantStaffFormValues>({ resolver: zodResolver(inviteMerchantStaffSchema) });

  async function onSubmit(values: InviteMerchantStaffFormValues) {
    setServerError(null);
    try {
      await inviteStaff.mutateAsync(values);
      reset();
      close();
    } catch {
      setServerError("Couldn't invite this staff member. The email may already be in use.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Invite Staff</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-6" noValidate>
          <div className="space-y-2">
            <Label htmlFor="fullName">Full Name</Label>
            <Input
              id="fullName"
              aria-invalid={!!errors.fullName}
              aria-describedby={errors.fullName ? "fullName-error" : undefined}
              {...register("fullName")}
            />
            {errors.fullName && <FieldError id="fullName-error">{errors.fullName.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              type="email"
              aria-invalid={!!errors.email}
              aria-describedby={errors.email ? "email-error" : undefined}
              {...register("email")}
            />
            {errors.email && <FieldError id="email-error">{errors.email.message}</FieldError>}
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">Temporary Password</Label>
            <Input
              id="password"
              type="password"
              aria-invalid={!!errors.password}
              aria-describedby={errors.password ? "password-error" : undefined}
              {...register("password")}
            />
            {errors.password && <FieldError id="password-error">{errors.password.message}</FieldError>}
          </div>
          {serverError && <FieldError id="merchant-staff-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Invite Staff Member"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
