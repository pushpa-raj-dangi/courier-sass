"use client";

import { useState } from "react";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
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
import { usePlatformSettings } from "@/features/platform-settings/hooks/usePlatformSettings";
import { useUpdatePlatformSetting } from "@/features/platform-settings/hooks/useUpdatePlatformSetting";
import { useResetPlatformSetting } from "@/features/platform-settings/hooks/useResetPlatformSetting";
import type { PlatformSetting, PlatformSettingCategory } from "@/features/platform-settings/types";

const CATEGORIES: { key: PlatformSettingCategory; label: string }[] = [
  { key: "PaymentGateway", label: "Payment Gateways" },
  { key: "Notifications", label: "Notifications" },
  { key: "Security", label: "Security" },
  { key: "Branding", label: "Branding & Flags" },
];

const CONFIRMATION_PHRASE = "REVOKE ALL SESSIONS";

function SettingField({ setting }: { setting: PlatformSetting }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [confirmText, setConfirmText] = useState("");
  const update = useUpdatePlatformSetting();
  const reset = useResetPlatformSetting();

  async function save(confirmationPhrase?: string) {
    await update.mutateAsync({ key: setting.key, value: draft, confirmationPhrase });
    setEditing(false);
    setDraft("");
    setConfirmOpen(false);
    setConfirmText("");
  }

  function handleSaveClick() {
    if (setting.requiresConfirmation) {
      setConfirmOpen(true);
      return;
    }
    void save();
  }

  const showEditableInput = editing || !setting.isSecret;

  return (
    <div className="space-y-2 border-b pb-4 last:border-b-0">
      <div className="flex items-center justify-between">
        <Label htmlFor={setting.key}>{setting.key}</Label>
        {setting.isOverridden && (
          <Button size="sm" variant="ghost" onClick={() => reset.mutate(setting.key)} disabled={reset.isPending}>
            Reset to default
          </Button>
        )}
      </div>
      {setting.description && <p className="text-sm text-muted-foreground">{setting.description}</p>}
      {showEditableInput ? (
        <div className="flex gap-2">
          <Input
            id={setting.key}
            type={setting.valueType === "Int" ? "number" : "text"}
            value={editing ? draft : (setting.displayValue ?? "")}
            placeholder={setting.isSecret ? "Enter a new value" : undefined}
            onChange={(e) => {
              setEditing(true);
              setDraft(e.target.value);
            }}
          />
          {editing && (
            <Button size="sm" onClick={handleSaveClick} disabled={update.isPending}>
              {update.isPending ? <Spinner className="h-4 w-4" /> : "Save"}
            </Button>
          )}
        </div>
      ) : (
        <div className="flex gap-2">
          <Input id={setting.key} value={setting.displayValue ?? "(not set)"} readOnly />
          <Button size="sm" variant="outline" onClick={() => setEditing(true)}>
            Replace
          </Button>
        </div>
      )}
      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Rotate {setting.key}?</AlertDialogTitle>
            <AlertDialogDescription>
              This immediately logs out every user across every organization on the platform. Type{" "}
              <strong>{CONFIRMATION_PHRASE}</strong> to confirm.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <Input value={confirmText} onChange={(e) => setConfirmText(e.target.value)} />
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction disabled={confirmText !== CONFIRMATION_PHRASE} onClick={() => void save(confirmText)}>
              Confirm
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

export default function PlatformSettingsPage() {
  const { data, isLoading, isError, refetch } = usePlatformSettings();

  if (isLoading) {
    return (
      <div className="space-y-4 p-6">
        <h1 className="font-display text-xl font-semibold">Platform Settings</h1>
        <Spinner className="h-6 w-6" />
      </div>
    );
  }
  if (isError || !data) {
    return (
      <ErrorState
        title="Couldn't load platform settings"
        description="Something went wrong fetching platform settings."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4 p-6">
      <h1 className="font-display text-xl font-semibold">Platform Settings</h1>
      <Tabs defaultValue="PaymentGateway">
        <TabsList>
          {CATEGORIES.map((c) => (
            <TabsTrigger key={c.key} value={c.key}>
              {c.label}
            </TabsTrigger>
          ))}
        </TabsList>
        {CATEGORIES.map((c) => (
          <TabsContent key={c.key} value={c.key} className="space-y-4">
            {data
              .filter((s) => s.category === c.key)
              .map((s) => (
                <SettingField key={s.key} setting={s} />
              ))}
          </TabsContent>
        ))}
      </Tabs>
    </div>
  );
}
