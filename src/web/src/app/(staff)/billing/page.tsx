"use client";

import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ErrorState } from "@/components/states/ErrorState";
import { LoadingState } from "@/components/states/LoadingState";
import { FieldError } from "@/components/ui/field-error";
import { DataTable, type ColumnDef } from "@/components/data-table/DataTable";
import { useBillingInvoices } from "@/features/billing/hooks/useBillingInvoices";
import { usePayInvoice } from "@/features/billing/hooks/usePayInvoice";
import { useSubscriptionStatus } from "@/features/billing/hooks/useSubscriptionStatus";
import { useAvailablePlans } from "@/features/billing/hooks/useAvailablePlans";
import { useAssignPlan } from "@/features/billing/hooks/useAssignPlan";
import { redirectToGateway } from "@/features/billing/GatewayRedirectForm";
import type { Invoice } from "@/features/billing/types";

function daysLeft(trialEndsAt: string): number {
  const ms = new Date(trialEndsAt).getTime() - Date.now();
  return Math.max(0, Math.ceil(ms / (1000 * 60 * 60 * 24)));
}

function PlanPicker() {
  const { data: plans, isLoading } = useAvailablePlans();
  const assignPlan = useAssignPlan();
  const [assignError, setAssignError] = useState<string | null>(null);

  const choose = async (planId: string) => {
    setAssignError(null);
    try {
      await assignPlan.mutateAsync(planId);
    } catch {
      setAssignError("Couldn't switch to that plan. Please try again.");
    }
  };

  if (isLoading) {
    return (
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3" role="status" aria-live="polite">
        <span className="sr-only">Loading</span>
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-28 w-full rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {assignError && <FieldError id="assign-plan-error">{assignError}</FieldError>}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {plans?.map((plan) => (
          <div key={plan.id} className="rounded-lg border p-4">
            <div className="flex items-center gap-2">
              <h3 className="font-semibold">{plan.name}</h3>
              {plan.isDefault && <Badge variant="secondary">Recommended</Badge>}
            </div>
            <p className="mt-1 text-sm text-muted-foreground">NPR {plan.monthlyPriceNpr}/month</p>
            <Button
              size="sm"
              className="mt-3"
              disabled={assignPlan.isPending}
              onClick={() => choose(plan.id)}
            >
              Choose this plan
            </Button>
          </div>
        ))}
      </div>
    </div>
  );
}

export default function BillingPage() {
  const { data: subscription, isLoading: subscriptionLoading } = useSubscriptionStatus();
  const { data: invoices, isLoading: invoicesLoading, isError, refetch } = useBillingInvoices();
  const payInvoice = usePayInvoice();
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

  if (subscriptionLoading || invoicesLoading) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-6 w-40" />
        <LoadingState rows={5} />
      </div>
    );
  }
  if (isError) {
    return <ErrorState title="Couldn't load billing" description="Something went wrong fetching your billing details." onRetry={() => refetch()} />;
  }

  const needsPlan = subscription?.status === "Trial" || subscription?.status === "TrialExpired";

  return (
    <div className="space-y-6">
      <div>
        <h1 className="font-display text-xl font-semibold">Billing</h1>
        {subscription?.status === "Active" && (
          <p className="mt-1 text-sm text-muted-foreground">Current plan: {subscription.planName} — NPR {subscription.monthlyPriceNpr}/month (Active)</p>
        )}
        {subscription?.status === "Trial" && subscription.trialEndsAt && (
          <p className="mt-1 text-sm text-muted-foreground">
            You&apos;re on a free trial — {daysLeft(subscription.trialEndsAt)} day{daysLeft(subscription.trialEndsAt) === 1 ? "" : "s"} left. Choose a plan below to keep going after it ends.
          </p>
        )}
        {subscription?.status === "TrialExpired" && (
          <p className="mt-1 text-sm text-destructive">Your trial has ended. Choose a plan below to continue using the app.</p>
        )}
        {(subscription?.status === "PastDue" || subscription?.status === "Cancelled") && (
          <p className="mt-1 text-sm text-muted-foreground">Current plan: {subscription.planName} ({subscription.status})</p>
        )}
      </div>

      {needsPlan && <PlanPicker />}

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
