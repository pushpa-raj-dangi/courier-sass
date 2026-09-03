"use client";

import { useState } from "react";
import { Check, Copy } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useCreateMerchantApiKey } from "@/features/merchant-api-keys/hooks/useCreateMerchantApiKey";
import { useMerchantApiKeyPanelStore } from "@/features/merchant-api-keys/store/merchantApiKeyPanelStore";
import type { ApiKeyCreated } from "@/features/merchant-api-keys/types";

// Every MerchantPermissionCatalog constant except ApiKeysManage — an issued key can never carry
// that scope itself (server-side refuses it unconditionally, ApiKeyService.CreateApiKeyAsync's
// own doc comment), so it's excluded from the picker rather than offered and silently dropped.
const AVAILABLE_SCOPES = [
  "merchant.profile.read",
  "merchant.profile.update",
  "merchant.staff.manage",
  "merchant.kyc.upload",
  "shipment.create",
  "shipment.read",
  "shipment.cancel",
  "merchant.webhooks.manage",
  "merchant.pickups.create",
  "merchant.pickups.read",
  "merchant.pickups.cancel",
  "merchant.cod.read",
  "merchant.pricing.read",
  "merchant.branches.read",
];

export function MerchantApiKeyCreateSheet() {
  const panel = useMerchantApiKeyPanelStore((s) => s.panel);
  const close = useMerchantApiKeyPanelStore((s) => s.close);
  const isOpen = panel.mode === "create";

  const createKey = useCreateMerchantApiKey();
  const [selectedScopes, setSelectedScopes] = useState<string[]>([]);
  const [createdKey, setCreatedKey] = useState<ApiKeyCreated | null>(null);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function toggleScope(scope: string, checked: boolean) {
    setSelectedScopes((prev) => (checked ? [...prev, scope] : prev.filter((s) => s !== scope)));
  }

  async function handleCreate() {
    setError(null);
    try {
      const result = await createKey.mutateAsync(selectedScopes.length > 0 ? selectedScopes : null);
      setCreatedKey(result);
    } catch {
      setError("Couldn't create an API key. Try again.");
    }
  }

  function handleClose(open: boolean) {
    if (!open) {
      close();
      setCreatedKey(null);
      setSelectedScopes([]);
      setCopied(false);
    }
  }

  async function handleCopy() {
    if (!createdKey) return;
    await navigator.clipboard.writeText(createdKey.plaintextKey);
    setCopied(true);
  }

  return (
    <Sheet open={isOpen} onOpenChange={handleClose}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Create API Key</SheetTitle>
        </SheetHeader>

        {!createdKey ? (
          <div className="mt-6 space-y-6">
            <div className="space-y-2">
              <Label>Scopes</Label>
              <p className="text-xs text-muted-foreground">
                Leave every scope unchecked to grant every permission your role holds. Select
                specific scopes for a narrower key.
              </p>
              <div className="max-h-64 space-y-2 overflow-y-auto rounded-lg border border-border p-3">
                {AVAILABLE_SCOPES.map((scope) => (
                  <div key={scope} className="flex items-center gap-2">
                    <Checkbox
                      id={`scope-${scope}`}
                      checked={selectedScopes.includes(scope)}
                      onCheckedChange={(checked) => toggleScope(scope, checked === true)}
                    />
                    <Label htmlFor={`scope-${scope}`} className="font-mono text-xs font-normal">
                      {scope}
                    </Label>
                  </div>
                ))}
              </div>
            </div>
            {error && <FieldError id="merchant-api-key-error">{error}</FieldError>}
            <Button type="button" onClick={handleCreate} disabled={createKey.isPending}>
              {createKey.isPending ? <Spinner className="h-4 w-4" /> : "Create API Key"}
            </Button>
          </div>
        ) : (
          <div className="mt-6 space-y-4">
            <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4">
              <p className="text-sm font-semibold text-foreground">Copy this key now</p>
              <p className="mt-1 text-xs text-muted-foreground">
                You won&apos;t be able to see this key again after closing this panel.
              </p>
              <div className="mt-3 flex items-center gap-2 rounded-md border border-border bg-background p-2">
                <code className="flex-1 overflow-x-auto text-xs">{createdKey.plaintextKey}</code>
                <Button type="button" variant="outline" size="icon" onClick={handleCopy} aria-label="Copy key">
                  {copied ? <Check className="h-4 w-4" strokeWidth={2} /> : <Copy className="h-4 w-4" strokeWidth={2} />}
                </Button>
              </div>
            </div>
            <Button type="button" variant="outline" onClick={() => handleClose(false)}>
              Done
            </Button>
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
