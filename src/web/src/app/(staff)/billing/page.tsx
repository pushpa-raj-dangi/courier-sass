"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { FieldError } from "@/components/ui/field-error";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBillingPlan } from "@/features/billing/hooks/useBillingPlan";
import { useBillingInvoices } from "@/features/billing/hooks/useBillingInvoices";
import { usePayInvoice } from "@/features/billing/hooks/usePayInvoice";
import { redirectToGateway } from "@/features/billing/GatewayRedirectForm";
import type { Invoice } from "@/features/billing/types";

export default function BillingPage() {
  const { data: plan, isLoading: planLoading } = useBillingPlan();
  const { data: invoices, isLoading: invoicesLoading, isError, refetch } = useBillingInvoices();
  const payInvoice = usePayInvoice();
  // The gateway call can genuinely fail (e.g. the gateway itself is unreachable or unconfigured,
  // as with a dev-only Khalti test key) — without this, mutateAsync's rejection would surface
  // only as an unhandled promise rejection with no on-screen feedback for the invoice being paid.
  const [payError, setPayError] = useState<string | null>(null);

  const pay = async (invoiceId: string, gateway: "Esewa" | "Khalti") => {
    setPayError(null);
    try {
      const checkoutInfo = await payInvoice.mutateAsync({ invoiceId, gateway });
      redirectToGateway(checkoutInfo);
    } catch {
      setPayError(`Couldn't start the ${gateway} checkout. Please try again.`);
    }
  };

  const columns: ColumnDef<Invoice>[] = [
    { key: "plan", header: "Plan", render: (i) => i.planName },
    { key: "amount", header: "Amount (NPR)", render: (i) => i.amountNpr },
    { key: "status", header: "Status", render: (i) => <Badge variant={i.status === "Overdue" ? "destructive" : i.status === "Paid" ? "default" : "secondary"}>{i.status}</Badge> },
    { key: "dueAt", header: "Due", render: (i) => new Date(i.dueAt).toLocaleDateString() },
    {
      key: "actions",
      header: "",
      render: (i) => (i.status === "Pending" || i.status === "Overdue") && (
        <div className="flex gap-2">
          <Button size="sm" variant="outline" disabled={payInvoice.isPending} onClick={() => pay(i.id, "Esewa")}>Pay with eSewa</Button>
          <Button size="sm" variant="outline" disabled={payInvoice.isPending} onClick={() => pay(i.id, "Khalti")}>Pay with Khalti</Button>
        </div>
      ),
    },
  ];

  if (planLoading || invoicesLoading) {
    return <div className="flex items-center justify-center p-12"><Spinner className="h-6 w-6" /></div>;
  }
  if (isError) {
    return <ErrorState title="Couldn't load billing" description="Something went wrong fetching your billing details." onRetry={() => refetch()} />;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="font-display text-xl font-semibold">Billing</h1>
        {plan && <p className="mt-1 text-sm text-muted-foreground">Current plan: {plan.planName} — NPR {plan.monthlyPriceNpr}/month ({plan.status})</p>}
      </div>
      {payError && <FieldError id="billing-pay-error">{payError}</FieldError>}
      <DataTable
        columns={columns}
        data={invoices && { items: invoices, page: 1, pageSize: invoices.length, totalCount: invoices.length, totalPages: 1 }}
        isLoading={false}
        emptyTitle="No invoices yet"
        emptyDescription="Invoices appear here once your plan's billing cycle generates one."
        onPageChange={() => {}}
        rowKey={(i) => i.id}
      />
    </div>
  );
}
