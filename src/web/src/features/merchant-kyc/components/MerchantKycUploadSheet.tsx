"use client";

import { useEffect, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { FieldError } from "@/components/ui/field-error";
import { SheetHeaderBlock } from "@/components/page/SheetHeaderBlock";
import { useUploadMerchantKycDocument } from "@/features/merchant-kyc/hooks/useUploadMerchantKycDocument";
import { useMerchantKycPanelStore } from "@/features/merchant-kyc/store/merchantKycPanelStore";
import type { KycDocumentType } from "@/features/merchant-kyc/types";

const DOCUMENT_TYPE_OPTIONS: { value: KycDocumentType; label: string }[] = [
  { value: "CitizenshipDocument", label: "Citizenship Document" },
  { value: "PanCard", label: "PAN Card" },
  { value: "BusinessRegistrationCertificate", label: "Business Registration Certificate" },
  { value: "BankAccountProof", label: "Bank Account Proof" },
  { value: "Other", label: "Other" },
];

// Mirrors MerchantKycEndpoints.cs's own AllowedContentTypes/MaxUploadSizeBytes exactly — a
// client-side pre-check for a fast error, not a substitute for the backend's own enforcement.
const ALLOWED_CONTENT_TYPES = ["application/pdf", "image/jpeg", "image/png"];
const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

export function MerchantKycUploadSheet() {
  const panel = useMerchantKycPanelStore((s) => s.panel);
  const close = useMerchantKycPanelStore((s) => s.close);
  const isOpen = panel.mode === "upload";

  const upload = useUploadMerchantKycDocument();
  const [documentType, setDocumentType] = useState<KycDocumentType>("CitizenshipDocument");
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Each open starts from the type the caller preselected, with a clean form.
  const openType = panel.mode === "upload" ? panel.documentType : null;
  useEffect(() => {
    if (openType) {
      setDocumentType(openType);
      setFile(null);
      setError(null);
    }
  }, [openType, isOpen]);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (!file) {
      setError("Choose a file to upload.");
      return;
    }
    if (!ALLOWED_CONTENT_TYPES.includes(file.type)) {
      setError("Only PDF, JPEG, or PNG files are supported.");
      return;
    }
    if (file.size > MAX_UPLOAD_BYTES) {
      setError("Files must be 10MB or smaller.");
      return;
    }
    try {
      await upload.mutateAsync({ documentType, file });
      setFile(null);
      close();
    } catch {
      setError("Couldn't upload this document. Try again.");
    }
  }

  return (
    <Sheet open={isOpen} onOpenChange={(open) => { if (!open) close(); }}>
      <SheetContent className="w-full overflow-y-auto sm:max-w-md">
        <SheetHeaderBlock
          eyebrow="KYC VERIFICATION"
          title="Upload KYC document"
          subtitle="PDF, JPEG or PNG. The courier reviews it before paying out your COD."
        />
        <form onSubmit={handleSubmit} className="mt-6 space-y-6" noValidate>
          <div className="space-y-2">
            <Label htmlFor="documentType">Document Type</Label>
            <Select value={documentType} onValueChange={(v) => setDocumentType(v as KycDocumentType)}>
              <SelectTrigger id="documentType" aria-label="Document type">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {DOCUMENT_TYPE_OPTIONS.map((o) => (
                  <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-2">
            <Label htmlFor="kyc-file">File</Label>
            <Input
              id="kyc-file"
              type="file"
              accept="application/pdf,image/jpeg,image/png"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
            <p className="text-xs text-muted-foreground">PDF, JPEG, or PNG. Max 10MB.</p>
          </div>
          {error && <FieldError id="kyc-upload-error">{error}</FieldError>}
          <Button type="submit" disabled={upload.isPending}>
            {upload.isPending ? <Spinner className="h-4 w-4" /> : "Upload"}
          </Button>
        </form>
      </SheetContent>
    </Sheet>
  );
}
