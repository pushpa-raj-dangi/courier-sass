"use client";

import { useEffect, useState } from "react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { buttonVariants } from "@/components/ui/button";
import { FieldError } from "@/components/ui/field-error";
import { Spinner } from "@/components/ui/spinner";
import { cn } from "@/lib/utils";
import { isRecentlyUsed, lastUsedLabel } from "@/features/merchant-api-keys/lib/apiKeyView";
import { pickupErrorMessage } from "@/features/merchant-pickups/lib/pickupErrorMessage";
import type { ApiKey } from "@/features/merchant-api-keys/types";

interface Props {
  apiKey: ApiKey | null;
  onOpenChange: (open: boolean) => void;
  /** Resolves on success (the page closes the dialog); rejects on failure (the dialog stays open). */
  onConfirm: (key: ApiKey) => Promise<unknown>;
}

export function RevokeApiKeyDialog({ apiKey, onOpenChange, onConfirm }: Props) {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const recent = apiKey ? isRecentlyUsed(apiKey.lastUsedAt) : false;
  const keyId = apiKey?.id;

  useEffect(() => {
    setError(null);
    setPending(false);
  }, [keyId]);

  async function handleConfirm() {
    if (!apiKey) return;
    setError(null);
    setPending(true);
    try {
      await onConfirm(apiKey);
    } catch (e) {
      setError(pickupErrorMessage(e, "Couldn't revoke this key. Try again."));
    } finally {
      setPending(false);
    }
  }

  return (
    <AlertDialog open={apiKey !== null} onOpenChange={(open) => !pending && onOpenChange(open)}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Revoke key {apiKey?.prefix}…?</AlertDialogTitle>
          <AlertDialogDescription>
            Anything using this key stops working right away.
            {apiKey && recent && ` It was last used ${lastUsedLabel(apiKey.lastUsedAt)}, so something is probably still calling it.`}{" "}
            This can&apos;t be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        {error && <FieldError id="revoke-api-key-error">{error}</FieldError>}
        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>Keep key</AlertDialogCancel>
          <AlertDialogAction
            className={cn(buttonVariants({ variant: "destructive" }))}
            disabled={pending}
            onClick={(e) => {
              // Radix closes the dialog on action click; hold it open until the request settles.
              e.preventDefault();
              void handleConfirm();
            }}
          >
            {pending ? <Spinner className="h-4 w-4" /> : "Revoke key"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
