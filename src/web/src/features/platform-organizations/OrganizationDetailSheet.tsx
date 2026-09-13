"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ErrorState } from "@/components/states/ErrorState";
import { DetailSkeleton } from "@/components/states/DetailSkeleton";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useOrganizationPanelStore } from "@/features/platform-organizations/store/organizationPanelStore";
import { usePlatformOrganization } from "@/features/platform-organizations/hooks/usePlatformOrganization";
import { useOrganizationUsers } from "@/features/platform-organizations/hooks/useOrganizationUsers";
import { useOrganizationBranches } from "@/features/platform-organizations/hooks/useOrganizationBranches";
import { useOrganizationInvoices } from "@/features/platform-organizations/hooks/useOrganizationInvoices";
import { useImpersonate } from "@/features/platform-organizations/hooks/useImpersonate";
import type { OrganizationUser, OrganizationBranch, OrganizationInvoice } from "@/features/platform-organizations/types";
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

const NESTED_PAGE_SIZE = 10;

function UsersSection({ organizationId, impersonate, router }: { organizationId: string; impersonate: ReturnType<typeof useImpersonate>; router: ReturnType<typeof useRouter> }) {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useOrganizationUsers(organizationId, page, NESTED_PAGE_SIZE);

  const columns: ColumnDef<OrganizationUser>[] = [
    { key: "name", header: "Name", render: (u) => u.fullName },
    { key: "email", header: "Email", render: (u) => u.email },
    { key: "status", header: "Status", render: (u) => (!u.isActive ? <Badge variant="secondary">Inactive</Badge> : null) },
    {
      key: "actions",
      header: "",
      render: (u) =>
        // Impersonation is only ever offered for active users — an inactive/disabled user
        // should never be usable as an impersonation identity.
        u.isActive ? (
          <Button
            size="sm"
            variant="outline"
            disabled={impersonate.isPending}
            onClick={() =>
              impersonate.mutate({ organizationId, userId: u.id }, { onSuccess: () => router.push("/dashboard") })
            }
          >
            Impersonate
          </Button>
        ) : null,
    },
  ];

  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold">Users{data ? ` (${data.totalCount})` : ""}</h3>
      <DataTable columns={columns} data={data} isLoading={isLoading} emptyTitle="No users" onPageChange={setPage} rowKey={(u) => u.id} />
    </section>
  );
}

function BranchesSection({ organizationId }: { organizationId: string }) {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useOrganizationBranches(organizationId, page, NESTED_PAGE_SIZE);

  const columns: ColumnDef<OrganizationBranch>[] = [
    { key: "code", header: "Code", render: (b) => b.code },
    { key: "name", header: "Name", render: (b) => b.name },
  ];

  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold">Branches{data ? ` (${data.totalCount})` : ""}</h3>
      <DataTable columns={columns} data={data} isLoading={isLoading} emptyTitle="No branches" onPageChange={setPage} rowKey={(b) => b.id} />
    </section>
  );
}

function InvoicesSection({ organizationId }: { organizationId: string }) {
  const [page, setPage] = useState(1);
  const { data, isLoading } = useOrganizationInvoices(organizationId, page, NESTED_PAGE_SIZE);

  const columns: ColumnDef<OrganizationInvoice>[] = [
    { key: "plan", header: "Plan", render: (i) => i.planName },
    { key: "amount", header: "Amount (NPR)", render: (i) => i.amountNpr },
    { key: "status", header: "Status", render: (i) => <Badge variant={i.status === "Overdue" ? "destructive" : i.status === "Paid" ? "default" : "secondary"}>{i.status}</Badge> },
  ];

  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold">Invoices{data ? ` (${data.totalCount})` : ""}</h3>
      <DataTable columns={columns} data={data} isLoading={isLoading} emptyTitle="No invoices" onPageChange={setPage} rowKey={(i) => i.id} />
    </section>
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
          <div className="p-4">
            <DetailSkeleton fieldRows={1} fieldsPerRow={2} />
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

            {organizationId && <UsersSection organizationId={organizationId} impersonate={impersonate} router={router} />}
            {organizationId && <BranchesSection organizationId={organizationId} />}
            {organizationId && <InvoicesSection organizationId={organizationId} />}

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
