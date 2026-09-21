import { Package } from "lucide-react";
import { RegisterForm } from "@/features/auth/RegisterForm";
import { AuthPageShell } from "@/components/auth/AuthPageShell";

export default function RegisterPage() {
  return (
    <AuthPageShell
      icon={Package}
      brand="Courier Platform"
      headline="Set up your organization."
      description="Branches, riders, merchants, and COD — one system for your whole courier operation, ready in minutes."
      switchPrompt="Already have an account?"
      switchLabel="Sign in"
      switchHref="/login"
    >
      <RegisterForm />
    </AuthPageShell>
  );
}
