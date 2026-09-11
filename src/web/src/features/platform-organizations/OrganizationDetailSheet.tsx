"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ErrorState } from "@/components/states/ErrorState";
import { useOrganizationPanelStore } from "@/features/platform-organizations/store/organizationPanelStore";
import { usePlatformOrganization } from "@/features/platform-organizations/hooks/usePlatformOrganization";
import { useImpersonate } from "@/features/platform-organizations/hooks/useImpersonate";
import { usePlatformPlansForSelect } from "@/features/platform-plans/hooks/usePlatformPlans";
import {
  useSuspendOrganization,
  useReactivateOrganization,
  useDeleteOrganization,
  useRestoreOrganization,
  useAssignOrganizationPlan,
} from "@/features/platform-organizations/hooks/useOrganizationLifecycleActions";

function AssignPlanControl({ organizationId }: { organizationId: string | null }) {
  const { data: plans } = usePlatformPlansForSelect();
  const assignPlan = useAssignOrganizationPlan();
  const [selectedPlanId, setSelectedPlanId] = useState<string>("");

  if (!organizationId) {
    return null;
  }

  return (
    <div className="flex items-center gap-2">
      <Select value={selectedPlanId} onValueChange={setSelectedPlanId}>
        <SelectTrigger className="w-48">
          <SelectValue placeholder="Assign a plan" />
        </SelectTrigger>
        <SelectContent>
          {plans?.filter((p) => p.isActive).map((plan) => (
            <SelectItem key={plan.id} value={plan.id}>{plan.name} — NPR {plan.monthlyPriceNpr}/mo</SelectItem>
          ))}
        </SelectContent>
      </Select>
      <Button
        size="sm"
        disabled={!selectedPlanId || assignPlan.isPending}
        onClick={() => assignPlan.mutate({ organizationId, planId: selectedPlanId })}
      >
        Assign Plan
      </Button>
    </div>
  );
}

export function OrganizationDetailSheet() {
  const router = useRouter();
  const panel = useOrganizationPanelStore((s) => s.panel);
  const close = useOrganizationPanelStore((s) => s.close);
  const organizationId = panel.mode === "detail" ? panel.organizationId : null;
  const { data, isLoading, isError, refetch } = usePlatformOrganization(organizationId);
  const suspend = useSuspendOrganization();
  const reactivate = useReactivateOrganization();
  const softDelete = useDeleteOrganization();
  const restore = useRestoreOrganization();
  const impersonate = useImpersonate();

  return (
    <Sheet open={panel.mode === "detail"} onOpenChange={(open) => !open && close()}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>{data?.organization.name ?? "Organization"}</SheetTitle>
        </SheetHeader>
        {isLoading && (
          <div className="flex items-center justify-center p-12">
            <Spinner className="h-6 w-6" />
          </div>
        )}
        {isError && (
          <ErrorState
            title="Couldn't load this organization"
            description="It may not exist, or something went wrong fetching its details."
            onRetry={() => refetch()}
          />
        )}
        {data && (
          <div className="space-y-6 p-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge
                variant={data.organization.deletedAt ? "destructive" : data.organization.isActive ? "default" : "secondary"}
              >
                {data.organization.deletedAt ? "Deleted" : data.organization.isActive ? "Active" : "Suspended"}
              </Badge>
              <span className="text-sm text-muted-foreground">
                {data.planName ?? (data.subscriptionStatus === "Trial" ? "On trial — no plan chosen" : data.subscriptionStatus === "TrialExpired" ? "Trial expired — no plan chosen" : "No plan assigned")}
              </span>
            </div>

            <AssignPlanControl organizationId={organizationId} />

            <div className="flex flex-wrap gap-2">
              {data.organization.isActive && !data.organization.deletedAt && (
                <Button
                  size="sm"
                  variant="destructive"
                  disabled={suspend.isPending}
                  onClick={() => organizationId && suspend.mutate(organizationId)}
                >
                  Suspend
                </Button>
              )}
              {!data.organization.isActive && !data.organization.deletedAt && (
                <Button
                  size="sm"
                  disabled={reactivate.isPending}
                  onClick={() => organizationId && reactivate.mutate(organizationId)}
                >
                  Reactivate
                </Button>
              )}
              {!data.organization.deletedAt && (
                <Button
                  size="sm"
                  variant="destructive"
                  disabled={softDelete.isPending}
                  onClick={() => organizationId && softDelete.mutate(organizationId)}
                >
                  Delete
                </Button>
              )}
              {data.organization.deletedAt && (
                <Button size="sm" disabled={restore.isPending} onClick={() => organizationId && restore.mutate(organizationId)}>
                  Restore
                </Button>
              )}
            </div>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Users ({data.users.length})</h3>
              <ul className="space-y-1 text-sm">
                {data.users.map((u) => (
                  <li key={u.id} className="flex items-center justify-between gap-2">
                    <span>
                      {u.fullName} — {u.email} {!u.isActive && <Badge variant="secondary">Inactive</Badge>}
                    </span>
                    {u.isActive && (
                      // Impersonation is only ever offered for active users — an inactive/disabled
                      // user should never be usable as an impersonation identity.
                      <Button
                        size="sm"
                        variant="outline"
                        disabled={impersonate.isPending}
                        onClick={() =>
                          organizationId &&
                          impersonate.mutate(
                            { organizationId, userId: u.id },
                            { onSuccess: () => router.push("/dashboard") }
                          )
                        }
                      >
                        Impersonate
                      </Button>
                    )}
                  </li>
                ))}
              </ul>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Branches ({data.branches.length})</h3>
              <ul className="space-y-1 text-sm">
                {data.branches.map((b) => (
                  <li key={b.id}>
                    {b.code} — {b.name}
                  </li>
                ))}
              </ul>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Invoices</h3>
              <ul className="space-y-1 text-sm">
                {data.invoices.map((i) => (
                  <li key={i.id}>
                    {i.planName} — NPR {i.amountNpr} — {i.status}
                  </li>
                ))}
              </ul>
            </section>

            <section>
              <h3 className="mb-2 text-sm font-semibold">Recent Activity</h3>
              <ul className="space-y-1 text-sm">
                {data.recentAuditEntries.map((entry) => (
                  <li key={entry.id}>
                    {entry.action} — {new Date(entry.timestamp).toLocaleString()}
                    {entry.impersonatedByUserId && " (via impersonation)"}
                  </li>
                ))}
              </ul>
            </section>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
