import { Package } from "lucide-react";
import { LoginForm } from "@/features/auth/LoginForm";
import { AuthPageShell } from "@/components/auth/AuthPageShell";

export default function LoginPage() {
  return (
    <AuthPageShell
      icon={Package}
      brand="Courier Platform"
      headline="Every parcel, accounted for."
      description="Scan-driven custody, hub transfers, delivery attempts and COD settlement — one system that always knows where a parcel is and who has it."
      switchPrompt="New here?"
      switchLabel="Create an organization"
      switchHref="/register"
    >
      <LoginForm />
    </AuthPageShell>
  );
}
