"use client";

import { Plus, ShieldOff } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantStaff } from "@/features/merchant-staff/hooks/useMerchantStaff";
import { useMerchantStaffPanelStore } from "@/features/merchant-staff/store/merchantStaffPanelStore";

export default function MerchantStaffPage() {
  // Owner-only screen. The nav entry (MerchantNavMain) already hides "Staff" from a Staff-role
  // session, and the backend (`MerchantStaffEndpoints`) already requires
  // `merchant.staff.manage` on both routes — this is defense in depth for anyone who navigates
  // to /merchant/staff directly: never issue the fetch, never render staff data.
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

  return <MerchantStaffList />;
}

function MerchantStaffList() {
  const { data: staff, isLoading, isError, refetch } = useMerchantStaff();
  const openCreate = useMerchantStaffPanelStore((s) => s.openCreate);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load staff"
        description="Something went wrong fetching your staff list."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">Staff</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          Invite Staff
        </Button>
      </div>
      {!staff || staff.length === 0 ? (
        <EmptyState title="No staff members yet" description="Invite your team to help manage shipments." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
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
                <TableRow key={s.id}>
                  <TableCell>{s.fullName}</TableCell>
                  <TableCell>{s.email}</TableCell>
                  <TableCell>{s.role}</TableCell>
                  <TableCell>
                    <Badge variant={s.isActive ? "secondary" : "outline"}>{s.isActive ? "Active" : "Inactive"}</Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
