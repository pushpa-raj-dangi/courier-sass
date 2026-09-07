import { FEATURES } from "../content";

export function FeatureGrid() {
  return (
    <section id="features" className="mx-auto max-w-6xl px-4 py-24">
      <div className="mb-12 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">
          Everything a courier operation actually runs on
        </h2>
        <p className="max-w-2xl text-marketing-muted-fg">
          Not an order tracker bolted onto a spreadsheet — the physical lifecycle of a parcel, modeled end to end.
        </p>
      </div>
      <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-3">
        {FEATURES.map((feature) => (
          <div
            key={feature.title}
            className="flex flex-col gap-3 rounded-xl border border-marketing-border bg-marketing-bg-elevated p-6"
          >
            <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-marketing-accent/15">
              <feature.icon className="h-5 w-5 text-marketing-accent" strokeWidth={2} />
            </span>
            <h3 className="font-display text-base font-semibold text-marketing-fg">{feature.title}</h3>
            <p className="text-sm text-marketing-muted-fg">{feature.description}</p>
          </div>
        ))}
      </div>
    </section>
  );
}
