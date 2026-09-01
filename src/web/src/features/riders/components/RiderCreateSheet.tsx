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
            <Input id="fullName" {...register("fullName")} />
            {errors.fullName && <p className="text-sm text-destructive">{errors.fullName.message}</p>}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input id="email" type="email" {...register("email")} />
              {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input id="password" type="password" {...register("password")} />
              {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="phone">Phone</Label>
              <Input id="phone" {...register("phone")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="licenseNumber">License Number</Label>
              <Input id="licenseNumber" {...register("licenseNumber")} />
              {errors.licenseNumber && <p className="text-sm text-destructive">{errors.licenseNumber.message}</p>}
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
                    <SelectTrigger id="baseBranchId" aria-label="Base branch">
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
              {errors.baseBranchId && <p className="text-sm text-destructive">{errors.baseBranchId.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="roleType">Role</Label>
              <Controller
                name="roleType"
                control={control}
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="roleType" aria-label="Role">
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
              {errors.roleType && <p className="text-sm text-destructive">{errors.roleType.message}</p>}
            </div>
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Rider"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
