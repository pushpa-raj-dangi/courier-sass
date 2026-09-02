"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { Lock } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { useAuthStore } from "@/lib/store/authStore";
import { useSession } from "@/features/auth/hooks/useSession";

export function ReauthDialog() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const sessionExpired = useAuthStore((s) => s.sessionExpired);
  const hideReauthDialog = useAuthStore((s) => s.hideReauthDialog);
  const { data: session } = useSession();

  const [password, setPassword] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: session?.email ?? "", password }),
    });
    setSubmitting(false);
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      setError(body.error ?? "Sign-in failed.");
      return;
    }
    setPassword("");
    hideReauthDialog();
    await queryClient.invalidateQueries();
  }

  async function handleSignInAsSomeoneElse() {
    await fetch("/api/auth/logout", { method: "POST" });
    hideReauthDialog();
    router.push("/login");
  }

  return (
    <Dialog open={sessionExpired}>
      <DialogContent
        onInteractOutside={(e) => e.preventDefault()}
        onEscapeKeyDown={(e) => e.preventDefault()}
        hideCloseButton
      >
        <DialogHeader>
          <DialogTitle className="font-display">Session expired</DialogTitle>
          <DialogDescription>
            Sign in again to keep going — you won&apos;t lose your place on this page.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="reauth-email">Email</Label>
            <Input id="reauth-email" type="email" value={session?.email ?? ""} disabled />
          </div>
          <div className="space-y-2">
            <Label htmlFor="reauth-password">Password</Label>
            <div className="relative">
              <Lock
                className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground"
                strokeWidth={2}
              />
              <Input
                id="reauth-password"
                type="password"
                autoComplete="current-password"
                className="pl-9"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoFocus
              />
            </div>
          </div>
          {error && <FieldError id="reauth-error">{error}</FieldError>}
          <DialogFooter className="flex-col gap-2 sm:flex-col">
            <Button type="submit" className="w-full" disabled={submitting}>
              {submitting ? <Spinner className="h-4 w-4" /> : "Sign in"}
            </Button>
            <Button
              type="button"
              variant="ghost"
              className="w-full"
              onClick={handleSignInAsSomeoneElse}
            >
              Sign in as someone else
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
