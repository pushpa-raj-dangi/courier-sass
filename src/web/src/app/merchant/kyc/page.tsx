"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { EmptyState } from "@/components/states/EmptyState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useMerchantKycDocuments } from "@/features/merchant-kyc/hooks/useMerchantKycDocuments";
import { useMerchantKycPanelStore } from "@/features/merchant-kyc/store/merchantKycPanelStore";

const DOCUMENT_TYPE_LABEL: Record<string, string> = {
  CitizenshipDocument: "Citizenship Document",
  PanCard: "PAN Card",
  BusinessRegistrationCertificate: "Business Registration Certificate",
  BankAccountProof: "Bank Account Proof",
  Other: "Other",
};

export default function MerchantKycPage() {
  const { data: documents, isLoading, isError, refetch } = useMerchantKycDocuments();
  const openUpload = useMerchantKycPanelStore((s) => s.openUpload);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorState
        title="Couldn't load KYC documents"
        description="Something went wrong fetching your documents."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="font-display text-xl font-semibold">KYC</h1>
        <Button size="sm" className="gap-1.5" onClick={() => openUpload()}>
          <Plus className="h-4 w-4" strokeWidth={2} />
          Upload Document
        </Button>
      </div>
      {!documents || documents.length === 0 ? (
        <EmptyState title="No documents uploaded" description="Upload your KYC documents to complete verification." />
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-muted/40">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Document Type</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {documents.map((d) => (
                <TableRow key={d.id}>
                  <TableCell>{DOCUMENT_TYPE_LABEL[d.documentType] ?? d.documentType}</TableCell>
                  <TableCell><StatusBadge status={d.status} namespace="Kyc" /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
