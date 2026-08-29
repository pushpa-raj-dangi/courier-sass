"use client";

import { useParams } from "next/navigation";
import { Spinner } from "@/components/ui/spinner";
import { ErrorState } from "@/components/states/ErrorState";
import { useBranch } from "@/features/branches/hooks/useBranch";
import { BranchRecord } from "@/features/branches/BranchRecord";

export default function BranchDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data: branch, isLoading, isError, refetch } = useBranch(id);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner className="h-6 w-6" />
      </div>
    );
  }

  if (isError || !branch) {
    return (
      <ErrorState
        title="Couldn't load this branch"
        description="It may not exist, or you may not have access to it."
        onRetry={() => refetch()}
      />
    );
  }

  return (
    <div className="space-y-6">
      <BranchRecord branch={branch} />
    </div>
  );
}
