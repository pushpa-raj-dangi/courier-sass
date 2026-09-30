"use client";

import { Plus, ShieldOff } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { LoadingState } from "@/components/states/LoadingState";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { BorderedTableContainer } from "@/components/data-table/BorderedTableContainer";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantApiKeys } from "@/features/merchant-api-keys/hooks/useMerchantApiKeys";
import { useRevokeMerchantApiKey } from "@/features/merchant-api-keys/hooks/useRevokeMerchantApiKey";
import { useMerchantApiKeyPanelStore } from "@/features/merchant-api-keys/store/merchantApiKeyPanelStore";

export default function MerchantApiKeysPage() {
  // Owner-only screen. The nav entry (merchantNav.ts) already hides "API Keys" from a Staff-role
  // session, and the backend already requires `merchant.apikeys.manage` on every route — this is
  // defense in depth for anyone who navigates to /merchant/api-keys directly: never issue the
  // fetch, never render key data.
  const hasApiKeyAccess = useMerchantPermission("merchant.apikeys.manage");

  if (!hasApiKeyAccess) {
    return (
      <EmptyState
        icon={ShieldOff}
        title="You don't have access to this page"
        description="Only the merchant account owner can view and manage API keys."
      />
    );
  }

  return <MerchantApiKeysList />;
}

function MerchantApiKeysList() {
  const { data: keys, isLoading, isError, refetch } = useMerchantApiKeys();
  const openCreate = useMerchantApiKeyPanelStore((s) => s.openCreate);
  const revokeKey = useRevokeMerchantApiKey();

  if (isLoading) {
    return <LoadingState rows={3} />;
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load API keys"
        description="Something went wrong fetching your API keys."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">API Keys</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          Create API Key
        </Button>
      </div>
      {!keys || keys.length === 0 ? (
        <EmptyState title="No API keys yet" description="Create a key to integrate with the Merchant API." />
      ) : (
        <BorderedTableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Prefix</TableHead>
                <TableHead>Scopes</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Created At</TableHead>
                <TableHead>Last Used</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {keys.map((k) => (
                <TableRow key={k.id}>
                  <TableCell className="font-mono text-xs">{k.prefix}</TableCell>
                  <TableCell className="text-xs">{k.scopes.length} scope{k.scopes.length === 1 ? "" : "s"}</TableCell>
                  <TableCell>
                    <Badge variant={k.isActive ? "secondary" : "outline"}>{k.isActive ? "Active" : "Revoked"}</Badge>
                  </TableCell>
                  <TableCell>{new Date(k.createdAt).toLocaleDateString()}</TableCell>
                  <TableCell>{k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleDateString() : "Never"}</TableCell>
                  <TableCell>
                    {k.isActive && (
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={revokeKey.isPending}
                        onClick={() => revokeKey.mutate(k.id)}
                      >
                        Revoke
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </BorderedTableContainer>
      )}
    </div>
  );
}
