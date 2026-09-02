"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useRoles } from "@/features/admin/hooks/useRoles";
import { useInviteUser } from "@/features/admin/hooks/useInviteUser";
import { useAdminPanelStore } from "@/features/admin/store/adminPanelStore";
import { adminErrorMessage } from "@/features/admin/lib/adminErrorMessage";
import { useBranches } from "@/features/branches/hooks/useBranches";

export function UserInviteSheet() {
  const panel = useAdminPanelStore((s) => s.panel);
  const close = useAdminPanelStore((s) => s.close);
  const isOpen = panel.mode === "user-invite";

  const { data: roles } = useRoles();
  const { data: branches } = useBranches();
  const inviteUser = useInviteUser();

  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [roleId, setRoleId] = useState("");
  const [branchId, setBranchId] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);

  function reset() {
    setEmail(""); setFullName(""); setPhone(""); setRoleId(""); setBranchId(""); setExpiresAt(""); setServerError(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setServerError(null);
    try {
      // Anchored to explicit UTC end-of-day, same fix Frontend Phase F7 needed for
      // MerchantSettlementCreateSheet — a bare "YYYY-MM-DD" 500s on Npgsql's timestamptz writer.
      await inviteUser.mutateAsync({
        email, fullName, phone: phone || undefined, roleId, branchId,
        expiresAt: expiresAt ? `${expiresAt}T23:59:59Z` : undefined,
      });
      reset();
      close();
    } catch (error) {
      setServerError(adminErrorMessage(error));
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) { reset(); close(); } }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Invite User</SheetTitle>
        </SheetHeader>
        <form onSubmit={handleSubmit} className="mt-6 space-y-4" noValidate>
          <div className="space-y-2">
            <Label htmlFor="invite-email">Email</Label>
            <Input id="invite-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-name">Full Name</Label>
            <Input id="invite-name" value={fullName} onChange={(e) => setFullName(e.target.value)} required />
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-phone">Phone</Label>
            <Input id="invite-phone" value={phone} onChange={(e) => setPhone(e.target.value)} />
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-role">Role</Label>
            <Select value={roleId} onValueChange={setRoleId}>
              <SelectTrigger id="invite-role" aria-label="Role">
                <SelectValue placeholder="Select role" />
              </SelectTrigger>
              <SelectContent>
                {roles?.map((r) => (
                  <SelectItem key={r.id} value={r.id}>{r.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-branch">Branch</Label>
            <Select value={branchId} onValueChange={setBranchId}>
              <SelectTrigger id="invite-branch" aria-label="Branch">
                <SelectValue placeholder="Select branch" />
              </SelectTrigger>
              <SelectContent>
                {branches?.map((b) => (
                  <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="invite-expires">Branch Access Expires (optional)</Label>
            <DatePicker id="invite-expires" value={expiresAt} onChange={setExpiresAt} clearable />
          </div>
          {serverError && <FieldError id="user-invite-server-error">{serverError}</FieldError>}
          <Button type="submit" disabled={inviteUser.isPending || !email || !fullName || !roleId || !branchId}>
            {inviteUser.isPending ? <Spinner className="h-4 w-4" /> : "Send Invite"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
