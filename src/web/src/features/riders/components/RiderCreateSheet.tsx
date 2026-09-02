"use client";

import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { createRiderSchema, type CreateRiderFormValues } from "@/features/riders/schema";
import { useCreateRider } from "@/features/riders/hooks/useCreateRider";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import { useBranches } from "@/features/branches/hooks/useBranches";

const ROLE_TYPES = ["PickupRider", "DeliveryRider"] as const;

export function RiderCreateSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const isOpen = panel.mode === "create-rider";

  const createRider = useCreateRider();
  const { data: branches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CreateRiderFormValues>({ resolver: zodResolver(createRiderSchema) });

  async function onSubmit(values: CreateRiderFormValues) {
    setServerError(null);
    try {
      await createRider.mutateAsync(values);
      reset();
      close();
    } catch {
      setServerError("Couldn't create the rider. Check the fields and try again.");
    }
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>New Rider</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="mt-6 space-y-4" noValidate>
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
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
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
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                aria-invalid={!!errors.password}
                aria-describedby={errors.password ? "password-error" : undefined}
                {...register("password")}
              />
              {errors.password && <FieldError id="password-error">{errors.password.message}</FieldError>}
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="phone">Phone</Label>
              <Input id="phone" {...register("phone")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="licenseNumber">License Number</Label>
              <Input
                id="licenseNumber"
                aria-invalid={!!errors.licenseNumber}
                aria-describedby={errors.licenseNumber ? "licenseNumber-error" : undefined}
                {...register("licenseNumber")}
              />
              {errors.licenseNumber && <FieldError id="licenseNumber-error">{errors.licenseNumber.message}</FieldError>}
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="baseBranchId">Base Branch</Label>
              <Controller
                name="baseBranchId"
                control={control}
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger
                      id="baseBranchId"
                      aria-label="Base branch"
                      aria-invalid={!!errors.baseBranchId}
                      aria-describedby={errors.baseBranchId ? "baseBranchId-error" : undefined}
                    >
                      <SelectValue placeholder="Select branch" />
                    </SelectTrigger>
                    <SelectContent>
                      {branches?.map((b) => (
                        <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {errors.baseBranchId && <FieldError id="baseBranchId-error">{errors.baseBranchId.message}</FieldError>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="roleType">Role</Label>
              <Controller
                name="roleType"
                control={control}
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger
                      id="roleType"
                      aria-label="Role"
                      aria-invalid={!!errors.roleType}
                      aria-describedby={errors.roleType ? "roleType-error" : undefined}
                    >
                      <SelectValue placeholder="Select role" />
                    </SelectTrigger>
                    <SelectContent>
                      {ROLE_TYPES.map((t) => (
                        <SelectItem key={t} value={t}>{t}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {errors.roleType && <FieldError id="roleType-error">{errors.roleType.message}</FieldError>}
            </div>
          </div>
          {serverError && <FieldError id="rider-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Rider"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
