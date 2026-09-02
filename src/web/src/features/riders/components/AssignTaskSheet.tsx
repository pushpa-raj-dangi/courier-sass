"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useShipment } from "@/features/shipments/hooks/useShipment";
import { useRiders } from "@/features/riders/hooks/useRiders";
import { useAssignTask } from "@/features/riders/hooks/useAssignTask";
import { useRiderPanelStore } from "@/features/riders/store/riderPanelStore";
import type { DeliveryTaskType } from "@/features/riders/types";

function inferTaskType(status: string): DeliveryTaskType | null {
  if (status === "Created") return "Pickup";
  if (status === "AtDestinationBranch") return "Delivery";
  return null;
}

export function AssignTaskSheet() {
  const panel = useRiderPanelStore((s) => s.panel);
  const close = useRiderPanelStore((s) => s.close);
  const isOpen = panel.mode === "assign-task";

  const [awbInput, setAwbInput] = useState("");
  const [lookupAwb, setLookupAwb] = useState("");
  const [riderId, setRiderId] = useState<string | undefined>(undefined);
  const [serverError, setServerError] = useState<string | null>(null);

  const { data: shipment, isFetching, isError } = useShipment(lookupAwb);
  const { data: riders } = useRiders();
  const assignTask = useAssignTask();

  const taskType = shipment ? inferTaskType(shipment.currentStatus) : null;
  const relevantBranchId = taskType === "Pickup" ? shipment?.originBranchId : shipment?.destinationBranchId;
  const eligibleRiders = riders?.filter((r) => r.isActive && r.baseBranchId === relevantBranchId);

  function reset() {
    setAwbInput("");
    setLookupAwb("");
    setRiderId(undefined);
    setServerError(null);
  }

  async function handleAssign() {
    if (!shipment || !taskType || !riderId) return;
    setServerError(null);
    try {
      await assignTask.mutateAsync({ riderId, shipmentId: shipment.id, type: taskType });
      reset();
      close();
    } catch {
      setServerError("Couldn't assign this task. Check the fields and try again.");
    }
  }

  return (
    <Sheet
      open={isOpen}
      onOpenChange={(open) => {
        if (!open) {
          reset();
          close();
        }
      }}
    >
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Assign Task</SheetTitle>
        </SheetHeader>
        <div className="mt-6 space-y-4">
          <div className="space-y-2">
            <Label htmlFor="assign-awb">AWB</Label>
            <Input
              id="assign-awb"
              aria-label="AWB"
              value={awbInput}
              onChange={(e) => setAwbInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  setLookupAwb(awbInput.trim());
                }
              }}
              placeholder="Scan or type an AWB, then press Enter"
            />
            {isFetching && <Spinner className="h-4 w-4" />}
            {isError && <FieldError id="assign-task-awb-error">No shipment found for that AWB.</FieldError>}
          </div>

          {shipment && (
            <div className="rounded-md border border-border p-3 text-sm">
              <p className="font-medium text-foreground">{shipment.recipientName}</p>
              <p className="text-muted-foreground">AWB {shipment.awb} &middot; {shipment.currentStatus}</p>
              {!taskType && (
                <p className="mt-2 text-destructive">
                  Shipment must be Created or AtDestinationBranch to assign a task, is currently {shipment.currentStatus}.
                </p>
              )}
            </div>
          )}

          {shipment && taskType && (
            <div className="space-y-2">
              <Label htmlFor="assign-rider">Rider</Label>
              <Select value={riderId} onValueChange={setRiderId}>
                <SelectTrigger id="assign-rider" aria-label="Rider">
                  <SelectValue placeholder="Select rider" />
                </SelectTrigger>
                <SelectContent>
                  {eligibleRiders?.map((r) => (
                    <SelectItem key={r.id} value={r.id}>{r.licenseNumber}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {serverError && <FieldError id="assign-task-server-error">{serverError}</FieldError>}
          <Button type="button" disabled={!shipment || !taskType || !riderId || assignTask.isPending} onClick={handleAssign}>
            {assignTask.isPending ? <Spinner className="h-4 w-4" /> : `Assign ${taskType ?? "Task"}`}
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
