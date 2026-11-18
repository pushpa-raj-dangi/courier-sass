"use client";

import { ShieldOff } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { BorderedTableContainer } from "@/components/data-table/BorderedTableContainer";
import { PageHeader } from "@/components/page/PageHeader";
import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { LoadingState } from "@/components/states/LoadingState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantSession } from "@/features/merchant-auth/hooks/useMerchantSession";
import { useMerchantStaff } from "@/features/merchant-staff/hooks/useMerchantStaff";
import { initials, roleCounts } from "@/features/merchant-staff/lib/staffView";
import { useMerchantStaffPanelStore } from "@/features/merchant-staff/store/merchantStaffPanelStore";
import { cn } from "@/lib/utils";

const ROLE_DESCRIPTION = {
  Owner: "Everything, plus staff and API keys",
  Staff: "Book shipments, request pickups, track parcels",
} as const;

export function MerchantStaffPage() {
  // Owner-only screen. The nav entry already hides "Staff" from a Staff-role session and the
  // backend requires `merchant.staff.manage` — this is defense in depth for a direct visit to
  // /merchant/staff: the guard sits above the inner component so the fetch never mounts.
  const hasStaffAccess = useMerchantPermission("merchant.staff.manage");

  if (!hasStaffAccess) {
    return (
      <EmptyState
        icon={ShieldOff}
        title="You don't have access to this page"
        description="Only the merchant account owner can view and manage staff."
      />
    );
  }

  return <MerchantStaffContent />;
}

function MerchantStaffContent() {
  const { data: staff, isLoading, isError, refetch } = useMerchantStaff();
  const { data: session } = useMerchantSession();
  const openCreate = useMerchantStaffPanelStore((s) => s.openCreate);
  const counts = staff ? roleCounts(staff) : null;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Staff"
        description={
          session
            ? `People who can book shipments and track parcels for ${session.merchantDisplayName}`
            : "People who can book shipments and track parcels"
        }
        actions={
          <Button size="sm" onClick={() => openCreate()}>
            Invite staff
          </Button>
        }
      />

      {isLoading && <LoadingState rows={5} />}
      {isError && (
        <ErrorState
          title="Couldn't load staff"
          description="Something went wrong fetching your staff list."
          onRetry={() => refetch()}
        />
      )}

      {staff && counts && (
        <>
          <section aria-label="What each role can do" className="grid gap-3.5 sm:grid-cols-2">
            {(["Owner", "Staff"] as const).map((role) => (
              <Card key={role} data-testid={`role-card-${role}`} className="space-y-1.5 px-4 py-4">
                <div className="flex items-baseline justify-between">
                  <span className="text-sm font-semibold">{role}</span>
                  <span className="font-display text-xl font-semibold">{counts[role]}</span>
                </div>
                <p className="text-xs text-muted-foreground">{ROLE_DESCRIPTION[role]}</p>
              </Card>
            ))}
          </section>

          {staff.length === 0 ? (
            <EmptyState title="No staff members yet" description="Invite your team to help manage shipments." />
          ) : (
            <BorderedTableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Name</TableHead>
                    <TableHead>Email</TableHead>
                    <TableHead>Role</TableHead>
                    <TableHead>Status</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {staff.map((s) => (
                    <TableRow
                      key={s.id}
                      data-testid="staff-row"
                      className={cn(!s.isActive && "text-muted-foreground")}
                    >
                      <TableCell>
                        <span className="flex items-center gap-2.5">
                          <Avatar className="h-8 w-8">
                            <AvatarFallback className="bg-primary/10 text-xs font-semibold text-primary">
                              {initials(s.fullName)}
                            </AvatarFallback>
                          </Avatar>
                          <span className="font-medium">{s.fullName}</span>
                        </span>
                      </TableCell>
                      <TableCell className="text-muted-foreground">{s.email}</TableCell>
                      <TableCell>{s.role}</TableCell>
                      <TableCell>
                        <StatusBadge status={s.isActive ? "Active" : "Inactive"} namespace="Staff" />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </BorderedTableContainer>
          )}
        </>
      )}
    </div>
  );
}
