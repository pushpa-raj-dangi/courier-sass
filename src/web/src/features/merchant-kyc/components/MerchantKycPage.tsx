"use client";

import { FileText } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/page/PageHeader";
import { SectionCard } from "@/components/page/SectionCard";
import { ErrorState } from "@/components/states/ErrorState";
import { LoadingState } from "@/components/states/LoadingState";
import { StatusBadge } from "@/components/status-badge/StatusBadge";
import { formatDate } from "@/features/cod/lib/codFormat";
import { useMerchantKycDocuments } from "@/features/merchant-kyc/hooks/useMerchantKycDocuments";
import { useMerchantKycPanelStore } from "@/features/merchant-kyc/store/merchantKycPanelStore";
import {
  KYC_TYPE_LABEL,
  kycProgress,
  otherDocs,
  requiredRows,
  type KycRow,
  type KycRowState,
} from "@/features/merchant-kyc/lib/kycView";
import type { KycDocumentType } from "@/features/merchant-kyc/types";
import { cn } from "@/lib/utils";

// Segment colours sit on the dark primary band, so they are lighter than the badge dots.
const SEGMENT_COLOR: Record<KycRowState, string> = {
  Approved: "hsl(168 45% 60%)",
  InReview: "hsl(38 80% 65%)",
  Rejected: "hsl(10 70% 68%)",
  NotUploaded: "hsl(0 0% 100% / 0.2)",
};

function metaLine(row: KycRow): string {
  const { doc } = row;
  if (!doc) return "Not uploaded yet";
  const uploaded = `Uploaded ${doc.uploadedAt ? formatDate(doc.uploadedAt) : "—"}`;
  if (!doc.reviewedAt || doc.status === "Pending") return uploaded;
  return `${uploaded} · ${doc.status === "Approved" ? "approved" : "rejected"} ${formatDate(doc.reviewedAt)}`;
}

function subLine(p: { rejected: number; inReview: number }): string | null {
  const parts = [
    p.rejected > 0 && `${p.rejected} needs a new upload`,
    p.inReview > 0 && `${p.inReview} waiting for review`,
  ].filter(Boolean);
  return parts.length ? parts.join(" · ") : null;
}

export function MerchantKycPage() {
  const { data: documents, isLoading, isError, refetch } = useMerchantKycDocuments();
  const openUpload = useMerchantKycPanelStore((s) => s.openUpload);

  const rows = documents ? requiredRows(documents) : null;
  const others = documents ? otherDocs(documents) : [];
  const progress = rows ? kycProgress(rows) : null;
  const sub = progress ? subLine(progress) : null;

  return (
    <div className="max-w-4xl space-y-5">
      <PageHeader title="KYC verification" description="The courier checks these documents before paying out your COD" />

      {isLoading && <LoadingState rows={4} />}
      {isError && (
        <ErrorState
          title="Couldn't load KYC documents"
          description="Something went wrong fetching your documents."
          onRetry={() => refetch()}
        />
      )}

      {rows && progress && (
        <>
          <section
            aria-label="Verification status"
            className="flex flex-wrap items-center gap-4 rounded-xl bg-primary px-5 py-4 text-primary-foreground"
          >
            <div className="min-w-0 flex-1 space-y-1">
              <p className="font-mono text-2xs uppercase tracking-[0.12em] text-primary-foreground/70">Verification</p>
              <p className="font-display text-xl font-semibold">{progress.approved} of 4 documents approved</p>
              {sub && <p className="text-xs text-primary-foreground/70">{sub}</p>}
            </div>
            <div aria-hidden="true" className="flex gap-1.5">
              {rows.map((r) => (
                <span key={r.type} className="h-2 w-11 rounded" style={{ backgroundColor: SEGMENT_COLOR[r.state] }} />
              ))}
            </div>
          </section>

          <SectionCard title="Required documents">
            <ul className="space-y-2.5">
              {rows.map((row) => (
                <KycDocRow key={row.type} row={row} onUpload={openUpload} />
              ))}
            </ul>
          </SectionCard>

          {others.length > 0 && (
            <SectionCard title="Other documents">
              <ul className="space-y-2.5">
                {others.map((d) => (
                  <KycDocRow
                    key={d.id}
                    row={{
                      type: "Other",
                      doc: d,
                      state: d.status === "Pending" ? "InReview" : d.status,
                    }}
                    onUpload={openUpload}
                  />
                ))}
              </ul>
            </SectionCard>
          )}

          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-dashed border-border px-4 py-3.5 text-sm text-muted-foreground">
            <span>Have another document the courier asked for?</span>
            <Button variant="outline" size="sm" onClick={() => openUpload("Other")}>
              Upload other document
            </Button>
          </div>
        </>
      )}
    </div>
  );
}

function KycDocRow({ row, onUpload }: { row: KycRow; onUpload: (type: KycDocumentType) => void }) {
  const action = row.state === "NotUploaded" ? "Upload" : row.state === "Rejected" ? "Upload again" : null;
  const reason = row.state === "Rejected" ? row.doc?.rejectionReason : null;
  return (
    <li
      data-testid="kyc-row"
      className={cn(
        "flex flex-wrap items-center gap-4 rounded-xl border bg-card px-4 py-3.5",
        row.state === "Rejected" ? "border-destructive/40" : "border-border",
      )}
    >
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-muted">
        <FileText className="h-5 w-5 text-muted-foreground" strokeWidth={1.75} aria-hidden="true" />
      </span>
      <div className="min-w-0 flex-1 space-y-0.5">
        <p data-testid="kyc-row-name" className="text-sm font-medium">{KYC_TYPE_LABEL[row.type]}</p>
        <p className="text-xs text-muted-foreground">{metaLine(row)}</p>
        {reason && <p className="text-xs text-destructive">Courier note: {reason}</p>}
      </div>
      <StatusBadge status={row.state} namespace="Kyc" />
      {action && (
        <Button size="sm" onClick={() => onUpload(row.type)}>
          {action}
        </Button>
      )}
    </li>
  );
}
