"use client";

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
import { cn } from "@/lib/utils";
import { isRecentlyUsed, lastUsedLabel } from "@/features/merchant-api-keys/lib/apiKeyView";
import type { ApiKey } from "@/features/merchant-api-keys/types";

interface Props {
  apiKey: ApiKey | null;
  onOpenChange: (open: boolean) => void;
  onConfirm: (key: ApiKey) => void;
}

export function RevokeApiKeyDialog({ apiKey, onOpenChange, onConfirm }: Props) {
  const recent = apiKey ? isRecentlyUsed(apiKey.lastUsedAt) : false;

  return (
    <AlertDialog open={apiKey !== null} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Revoke key {apiKey?.prefix}…?</AlertDialogTitle>
          <AlertDialogDescription>
            Anything using this key stops working right away.
            {apiKey && recent && ` It was last used ${lastUsedLabel(apiKey.lastUsedAt)}, so something is probably still calling it.`}{" "}
            This can&apos;t be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep key</AlertDialogCancel>
          <AlertDialogAction
            className={cn(buttonVariants({ variant: "destructive" }))}
            onClick={() => apiKey && onConfirm(apiKey)}
          >
            Revoke key
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
