import { PROMISE_ITEMS } from "../content";
import { MonoLabel } from "./MonoLabel";

/** Three-claim strip between Hero and the thread — the visual hand-off from atmosphere to structure. */
export function PromiseStrip() {
  return (
    <div className="border-y border-marketing-border/60 bg-marketing-bg-elevated/50">
      <ul className="mx-auto grid max-w-6xl divide-y divide-marketing-border/60 px-4 md:grid-cols-3 md:divide-x md:divide-y-0">
        {PROMISE_ITEMS.map((item) => (
          <li key={item.numeral} className="flex flex-col gap-2 py-6 md:px-8 md:first:pl-0 md:last:pr-0">
            <MonoLabel className="text-marketing-accent">{item.numeral}</MonoLabel>
            <p className="font-display text-lg font-semibold text-marketing-fg">{item.title}</p>
            <p className="text-sm text-marketing-muted-fg">{item.body}</p>
          </li>
        ))}
      </ul>
    </div>
  );
}
