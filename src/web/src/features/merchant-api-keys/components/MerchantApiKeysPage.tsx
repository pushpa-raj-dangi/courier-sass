"use client";

import { useState } from "react";
import { Plus, ShieldOff } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { BorderedTableContainer } from "@/components/data-table/BorderedTableContainer";
import { PageHeader } from "@/components/page/PageHeader";
import { EmptyState } from "@/components/states/EmptyState";
import { ErrorState } from "@/components/states/ErrorState";
import { LoadingState } from "@/components/states/LoadingState";
import { formatDate } from "@/features/cod/lib/codFormat";
import { useMerchantPermission } from "@/features/merchant-auth/hooks/useMerchantPermission";
import { useMerchantApiKeys } from "@/features/merchant-api-keys/hooks/useMerchantApiKeys";
import { useRevokeMerchantApiKey } from "@/features/merchant-api-keys/hooks/useRevokeMerchantApiKey";
import { useMerchantApiKeyPanelStore } from "@/features/merchant-api-keys/store/merchantApiKeyPanelStore";
import { lastUsedLabel, splitKeys } from "@/features/merchant-api-keys/lib/apiKeyView";
import { scopeLabel } from "@/features/merchant-api-keys/lib/scopeLabels";
import { RevokeApiKeyDialog } from "@/features/merchant-api-keys/components/RevokeApiKeyDialog";
import type { ApiKey } from "@/features/merchant-api-keys/types";

type KeyTab = "active" | "revoked";

export function MerchantApiKeysPage() {
  // Owner-only screen. The nav entry already hides "API Keys" from a Staff-role session and the
  // backend requires `merchant.apikeys.manage` on every route — this is defense in depth for a
  // direct visit to /merchant/api-keys: the guard sits above the inner component so the fetch
  // never mounts and no key data renders.
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

  return <MerchantApiKeysContent />;
}

function MerchantApiKeysContent() {
  const { data: keys, isLoading, isError, refetch } = useMerchantApiKeys();
  const openCreate = useMerchantApiKeyPanelStore((s) => s.openCreate);
  const revokeKey = useRevokeMerchantApiKey();
  const [tab, setTab] = useState<KeyTab>("active");
  const [pendingRevoke, setPendingRevoke] = useState<ApiKey | null>(null);

  const split = keys ? splitKeys(keys) : null;
  const rows = split ? split[tab] : [];

  return (
    <div className="space-y-5">
      <PageHeader
        title="API keys"
        description="Keys let your store or software book shipments through the Merchant API"
        actions={
          <Button size="sm" className="gap-1.5" onClick={() => openCreate()}>
            <Plus className="h-4 w-4" strokeWidth={2} />
            Create API key
          </Button>
        }
      />

      <Tabs value={tab} onValueChange={(v) => setTab(v as KeyTab)}>
        <TabsList>
          <TabsTrigger value="active" className="gap-1.5">
            Active <span className="tabular-nums text-muted-foreground">{split ? split.active.length : "—"}</span>
          </TabsTrigger>
          <TabsTrigger value="revoked" className="gap-1.5">
            Revoked <span className="tabular-nums text-muted-foreground">{split ? split.revoked.length : "—"}</span>
          </TabsTrigger>
        </TabsList>
      </Tabs>

      {isLoading && <LoadingState rows={3} />}
      {isError && (
        <ErrorState
          title="Couldn't load API keys"
          description="Something went wrong fetching your API keys."
          onRetry={() => refetch()}
        />
      )}
      {split && rows.length === 0 && (
        <EmptyState
          title={tab === "active" ? "No active API keys" : "No revoked keys"}
          description={
            tab === "active"
              ? "Create a key to connect your store or software to the Merchant API."
              : "Keys you revoke show up here."
          }
        />
      )}
      {split && rows.length > 0 && (
        <BorderedTableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Key</TableHead>
                <TableHead>Can do</TableHead>
                <TableHead>Created</TableHead>
                <TableHead>Last used</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((k) => (
                <TableRow key={k.id}>
                  <TableCell className="font-mono text-xs">{k.prefix}…</TableCell>
                  <TableCell>
                    <div className="flex flex-wrap gap-1">
                      {k.scopes.map((s) => (
                        <Badge key={s} variant="outline">{scopeLabel(s)}</Badge>
                      ))}
                    </div>
                  </TableCell>
                  <TableCell>{formatDate(k.createdAt)}</TableCell>
                  <TableCell>{lastUsedLabel(k.lastUsedAt)}</TableCell>
                  <TableCell className="text-right">
                    {k.isActive ? (
                      <Button
                        variant="outline"
                        size="sm"
                        className="border-destructive/40 text-destructive hover:bg-destructive/5 hover:text-destructive"
                        disabled={revokeKey.isPending}
                        onClick={() => setPendingRevoke(k)}
                      >
                        Revoke
                      </Button>
                    ) : (
                      <span className="text-xs text-muted-foreground">
                        Revoked {k.revokedAt ? formatDate(k.revokedAt) : ""}
                      </span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </BorderedTableContainer>
      )}

      <RevokeApiKeyDialog
        apiKey={pendingRevoke}
        onOpenChange={(open) => {
          if (!open) setPendingRevoke(null);
        }}
        onConfirm={(k) => {
          revokeKey.mutate(k.id);
          setPendingRevoke(null);
        }}
      />
    </div>
  );
}
