import { CREDIBILITY_POINTS, PERMISSION_SCOPE_EXAMPLE } from "../content";
import { MockCard } from "./MockCard";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";

export function RolesSection() {
  const example = PERMISSION_SCOPE_EXAMPLE;
  return (
    <SectionShell
      id="roles"
      index={5}
      eyebrow="Roles & branches"
      title="Permission plus branch scope. Never one without the other."
      lede="Access is granted by permission and limited to the branches a user actually works in — enforced on the server, so a tampered ID gets nothing."
      tone="elevated"
    >
      <div className="grid gap-12 lg:grid-cols-[1fr_1.2fr] lg:items-start">
        <MockCard label={example.role}>
          <MonoLabel>Permissions</MonoLabel>
          <ul className="mt-2 flex flex-col gap-1.5 font-mono text-sm text-marketing-fg">
            {example.permissions.map((permission) => (
              <li key={permission}>{permission}</li>
            ))}
          </ul>
          <div className="mt-4 border-t border-marketing-border pt-3">
            <MonoLabel>Branch scope</MonoLabel>
            <p className="mt-1 text-sm text-marketing-fg">{example.branch}</p>
          </div>
        </MockCard>
        <div className="flex flex-col gap-3">
          <MonoLabel>What&apos;s already true</MonoLabel>
          <p className="max-w-xl text-sm text-marketing-muted-fg">
            No case studies yet — this is still an early platform. Today, these hold:
          </p>
          <div className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
            {CREDIBILITY_POINTS.map((point) => (
              <div key={point.title} className="flex flex-col gap-1.5 py-5 sm:flex-row sm:gap-6">
                <h3 className="font-display text-base font-semibold text-marketing-fg sm:w-56 sm:shrink-0">
                  {point.title}
                </h3>
                <p className="text-sm text-marketing-muted-fg">{point.description}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </SectionShell>
  );
}
