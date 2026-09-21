"use client";

import { useState, type ReactNode } from "react";
import { Eye, EyeOff } from "lucide-react";
import { Label } from "@/components/ui/label";
import { FieldError } from "@/components/ui/field-error";

/**
 * Shared form vocabulary for the staff auth pages (login, register): one control height,
 * one text size, one focus/invalid treatment, one field anatomy. Keeping it here is what
 * keeps the two forms from drifting apart.
 *
 * Compact 40px controls at 14px text. Input's own base uses 16px below `md` so iOS doesn't
 * zoom on focus; `md:text-sm` sets the one desktop size for every control.
 */
export const authInputClass =
  "h-10 bg-background shadow-none hover:border-foreground/30 focus-visible:border-ring focus-visible:ring-2 aria-[invalid=true]:border-destructive aria-[invalid=true]:focus-visible:ring-destructive md:text-sm";

export const authButtonClass = "group h-10 w-full";

export interface FieldRenderProps {
  id: string;
  "aria-invalid": boolean;
  "aria-describedby": string | undefined;
}

/** Label, control, then either the validation error or the hint — never both, so the eye lands on one line. */
export function Field({
  id,
  label,
  hint,
  error,
  className,
  children,
}: {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  className?: string;
  children: (props: FieldRenderProps) => ReactNode;
}) {
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  return (
    <div className={className ? `space-y-1 ${className}` : "space-y-1"}>
      <Label htmlFor={id}>{label}</Label>
      {children({
        id,
        "aria-invalid": !!error,
        "aria-describedby": error ? errorId : hint ? hintId : undefined,
      })}
      {error ? (
        <FieldError id={errorId}>{error}</FieldError>
      ) : hint ? (
        <p id={hintId} className="text-xs leading-snug text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  );
}

/** Page title block: same scale and rhythm on every auth form. */
export function AuthFormHeader({ title, description }: { title: string; description: string }) {
  return (
    <header className="mb-5 space-y-1">
      <h1 className="font-display text-2xl font-semibold leading-tight tracking-tight text-balance">{title}</h1>
      <p className="text-sm text-muted-foreground">{description}</p>
    </header>
  );
}

/** Show/hide toggle overlaid on a password input; 32px target inside the 40px control. */
export function PasswordToggle({ shown, onToggle }: { shown: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-label={shown ? "Hide password" : "Show password"}
      aria-pressed={shown}
      className="absolute right-1 top-1/2 flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      {shown ? <EyeOff className="h-4 w-4" strokeWidth={2} /> : <Eye className="h-4 w-4" strokeWidth={2} />}
    </button>
  );
}

export function usePasswordVisibility() {
  const [shown, setShown] = useState(false);
  return { shown, toggle: () => setShown((v) => !v) };
}
