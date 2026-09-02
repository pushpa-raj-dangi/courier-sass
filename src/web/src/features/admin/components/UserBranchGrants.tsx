"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/states/EmptyState";
import { useUserBranchGrants } from "@/features/admin/hooks/useUserBranchGrants";
import { useGrantBranch } from "@/features/admin/hooks/useGrantBranch";
import { useRevokeBranch } from "@/features/admin/hooks/useRevokeBranch";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";
import { useBranches } from "@/features/branches/hooks/useBranches";
import { usePermission } from "@/features/auth/hooks/usePermission";

export function UserBranchGrants({ userId }: { userId: string }) {
  const { data: grants, isLoading } = useUserBranchGrants(userId);
  const { data: branches } = useBranches();
  const grantBranch = useGrantBranch(userId);
  const revokeBranch = useRevokeBranch(userId);
  const canUpdate = usePermission("user.update");

  const [adding, setAdding] = useState(false);
  const [branchId, setBranchId] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);

  async function handleAdd() {
    setServerError(null);
    try {
      await grantBranch.mutateAsync({ branchId, expiresAt: expiresAt ? `${expiresAt}T23:59:59Z` : undefined });
      setAdding(false);
      setBranchId("");
      setExpiresAt("");
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  async function handleRevoke(revokeBranchId: string) {
    setServerError(null);
    try {
      await revokeBranch.mutateAsync(revokeBranchId);
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  if (isLoading) {
    return <Spinner className="h-5 w-5" />;
  }

  return (
    <div className="space-y-3">
      <Label>Branch Access</Label>
      {(!grants || grants.length === 0) && !adding && <EmptyState title="No branch grants" description="This user has no branch access yet." />}
      <div className="space-y-2">
        {grants?.map((g) => (
          <div key={g.branchId} className="flex items-center justify-between rounded-md border border-border p-3 text-sm">
            <div>
              <p className="font-medium text-foreground">{g.branchName}</p>
              <p className="text-muted-foreground">{g.expiresAt ? `Expires ${new Date(g.expiresAt).toLocaleDateString()}` : "No expiry"}</p>
            </div>
            {canUpdate && (
              <Button type="button" size="sm" variant="outline" onClick={() => handleRevoke(g.branchId)} disabled={revokeBranch.isPending}>
                Revoke
              </Button>
            )}
          </div>
        ))}
      </div>
      {canUpdate && !adding && (
        <Button type="button" size="sm" variant="outline" onClick={() => setAdding(true)}>Add Grant</Button>
      )}
      {adding && (
        <div className="space-y-2 rounded-md border border-border p-3">
          <Select value={branchId} onValueChange={setBranchId}>
            <SelectTrigger aria-label="Branch">
              <SelectValue placeholder="Select branch" />
            </SelectTrigger>
            <SelectContent>
              {branches?.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Input type="date" aria-label="Expires at (optional)" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
          {serverError && <p className="text-sm text-destructive">{serverError}</p>}
          <div className="flex gap-2">
            <Button type="button" size="sm" disabled={!branchId || grantBranch.isPending} onClick={handleAdd}>
              {grantBranch.isPending ? <Spinner className="h-4 w-4" /> : "Grant"}
            </Button>
            <Button type="button" size="sm" variant="outline" onClick={() => setAdding(false)}>Cancel</Button>
          </div>
        </div>
      )}
    </div>
  );
}
