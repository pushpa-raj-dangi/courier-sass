"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { GeographyPicker, type GeographyValue } from "@/features/branches/GeographyPicker";
import { createBranchSchema, type CreateBranchFormValues } from "@/features/branches/schema";
import { useCreateBranch } from "@/features/branches/hooks/useCreateBranch";
import { useBranches } from "@/features/branches/hooks/useBranches";

const BRANCH_TYPES = ["HeadOffice", "Hub", "Branch", "PickupPoint", "Warehouse"] as const;

export default function NewBranchPage() {
  const router = useRouter();
  const createBranch = useCreateBranch();
  const { data: existingBranches } = useBranches();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<CreateBranchFormValues>({ resolver: zodResolver(createBranchSchema) });

  const geographyValue: GeographyValue = {
    provinceId: watch("provinceId") ?? null,
    districtId: watch("districtId") ?? null,
    localLevelId: watch("localLevelId") ?? null,
    wardNumber: watch("wardNumber") ?? null,
  };

  async function onSubmit(values: CreateBranchFormValues) {
    setServerError(null);
    try {
      const created = await createBranch.mutateAsync(values);
      router.push(`/branches/${created.id}`);
    } catch {
      setServerError("Couldn't create the branch. Check the fields and try again.");
    }
  }

  return (
    <Card className="max-w-2xl">
      <CardHeader>
        <CardTitle className="font-display text-lg">New Branch</CardTitle>
      </CardHeader>
      <CardContent>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="code">Code</Label>
              <Input id="code" {...register("code")} />
              {errors.code && <p className="text-sm text-destructive">{errors.code.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="name">Name</Label>
              <Input id="name" {...register("name")} />
              {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="type">Type</Label>
            <Controller
              name="type"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="type" aria-label="Type">
                    <SelectValue placeholder="Select type" />
                  </SelectTrigger>
                  <SelectContent>
                    {BRANCH_TYPES.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {errors.type && <p className="text-sm text-destructive">{errors.type.message}</p>}
          </div>
          <GeographyPicker
            value={geographyValue}
            onChange={(next) => {
              setValue("provinceId", next.provinceId ?? "");
              setValue("districtId", next.districtId ?? "");
              setValue("localLevelId", next.localLevelId ?? "");
              setValue("wardNumber", next.wardNumber ?? undefined as never);
            }}
          />
          <div className="space-y-2">
            <Label htmlFor="addressLine">Address</Label>
            <Input id="addressLine" {...register("addressLine")} />
            {errors.addressLine && (
              <p className="text-sm text-destructive">{errors.addressLine.message}</p>
            )}
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="phone">Phone</Label>
              <Input id="phone" {...register("phone")} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="operatingHours">Operating Hours</Label>
              <Input id="operatingHours" placeholder="9:00 AM - 6:00 PM" {...register("operatingHours")} />
            </div>
          </div>
          <div className="space-y-2">
            <Label htmlFor="parentBranchId">Parent Branch</Label>
            <Controller
              name="parentBranchId"
              control={control}
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger id="parentBranchId" aria-label="Parent branch">
                    <SelectValue placeholder="None" />
                  </SelectTrigger>
                  <SelectContent>
                    {existingBranches?.map((b) => (
                      <SelectItem key={b.id} value={b.id}>
                        {b.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? <Spinner className="h-4 w-4" /> : "Create Branch"}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
