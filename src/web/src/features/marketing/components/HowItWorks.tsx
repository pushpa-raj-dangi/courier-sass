import { HOW_IT_WORKS_STEPS } from "../content";
import { LifecycleRail } from "./LifecycleRail";

export function HowItWorks() {
  return (
    <section id="how-it-works" className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">How it works</h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          Four stages, each backed by a real scan, manifest, or delivery attempt — never an arbitrary status edit.
        </p>
      </div>
      <div className="mb-6 hidden px-2 lg:block">
        <LifecycleRail
          dotSize="lg"
          stops={HOW_IT_WORKS_STEPS.map((step) => ({ tone: step.tone, label: step.title }))}
        />
      </div>
      <ol className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
        {HOW_IT_WORKS_STEPS.map((step) => (
          <li
            key={step.step}
            className="flex flex-col gap-2 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6"
          >
            <span className="font-mono text-xs text-marketing-accent">{step.step}</span>
            <h3 className="font-display text-base font-semibold text-marketing-fg">{step.title}</h3>
            <p className="text-sm text-marketing-muted-fg">{step.description}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}
