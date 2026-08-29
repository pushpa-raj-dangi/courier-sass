import { Badge } from "@/components/ui/badge";
import { resolveStatusConfig } from "@/components/status-badge/statusConfig";

// A neutral pill with a small color-coded dot, not a saturated colored fill — several status
// tokens (verified in Task 3's contrast pass) aren't safe as text-on-fill at every lightness,
// while a small dot only needs to be visible (3:1), not carry text-weight contrast. The label
// text always stays the badge's normal ink-on-neutral color, so meaning is never color-only.
export function StatusBadge({ status }: { status: string }) {
  const config = resolveStatusConfig(status);
  return (
    <Badge variant="secondary" className="gap-1.5">
      <span
        className="h-2 w-2 rounded-full"
        style={{ backgroundColor: `hsl(var(--${config.token}))` }}
        aria-hidden="true"
      />
      {config.label}
    </Badge>
  );
}
