import { PRICING_FAQ } from "../content";

export function PricingFaq() {
  return (
    <section className="mx-auto max-w-3xl px-4 pb-24">
      <div className="mb-10 flex flex-col items-center gap-3 text-center">
        <h2 className="font-display text-3xl font-semibold text-marketing-fg sm:text-4xl">Before you pick a plan</h2>
        <p className="max-w-2xl text-marketing-muted-fg">What the trial and plan limits actually do, in plain terms.</p>
      </div>
      <div className="flex flex-col divide-y divide-marketing-border/60 border-y border-marketing-border/60">
        {PRICING_FAQ.map((faq) => (
          <div key={faq.question} className="flex flex-col gap-1.5 py-6 sm:flex-row sm:gap-6">
            <h3 className="font-display text-base font-semibold text-marketing-fg sm:w-72 sm:shrink-0">
              {faq.question}
            </h3>
            <p className="text-sm text-marketing-muted-fg">{faq.answer}</p>
          </div>
        ))}
      </div>
    </section>
  );
}
