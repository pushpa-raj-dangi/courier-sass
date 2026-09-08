"use client";

import { useFieldArray, useFormContext } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FieldError } from "@/components/ui/field-error";
import type { CreateShipmentFormValues } from "@/features/shipments/schema";

export function ItemsField() {
  const { control, register, formState } = useFormContext<CreateShipmentFormValues>();
  const { fields, append, remove } = useFieldArray({ control, name: "items" });

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <Label>Items</Label>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="gap-1.5"
          onClick={() => append({ description: "", quantity: 1, unitValue: 0 })}
        >
          <Plus className="h-3.5 w-3.5" strokeWidth={2} />
          Add Item
        </Button>
      </div>
      {fields.map((field, index) => (
        <div key={field.id} className="grid grid-cols-[1fr_auto_auto_auto] items-end gap-2">
          <div className="min-w-0 space-y-1">
            <Label htmlFor={`items.${index}.description`}>Description</Label>
            <Input id={`items.${index}.description`} {...register(`items.${index}.description`)} />
          </div>
          <div className="w-20 space-y-1">
            <Label htmlFor={`items.${index}.quantity`}>Qty</Label>
            <Input
              id={`items.${index}.quantity`}
              type="number"
              {...register(`items.${index}.quantity`, { valueAsNumber: true })}
            />
          </div>
          <div className="w-28 space-y-1">
            <Label htmlFor={`items.${index}.unitValue`}>Unit Value</Label>
            <Input
              id={`items.${index}.unitValue`}
              type="number"
              {...register(`items.${index}.unitValue`, { valueAsNumber: true })}
            />
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            aria-label="Remove item"
            onClick={() => remove(index)}
          >
            <Trash2 className="h-4 w-4" strokeWidth={2} />
          </Button>
        </div>
      ))}
      {formState.errors.items?.message && (
        <FieldError id="items-error">{formState.errors.items.message}</FieldError>
      )}
    </div>
  );
}
