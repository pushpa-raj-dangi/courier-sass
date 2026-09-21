import { SCREENSHOTS } from "../content";
import { AppScreenshotFrame } from "./AppScreenshotFrame";
import { MonoLabel } from "./MonoLabel";
import { SectionShell } from "./SectionShell";

export function ScreenShowcase() {
  return (
    <SectionShell
      id="screens"
      index={6}
      eyebrow="Screens"
      title="The actual product, not a mockup."
      lede="Every screen below is the real app, running against fictional seed data."
    >
      <div className="grid grid-cols-1 gap-x-8 gap-y-12 lg:grid-cols-2">
        {SCREENSHOTS.map((screenshot, index) => (
          <figure key={screenshot.id} className={`flex flex-col gap-3 ${index % 2 === 1 ? "lg:mt-16" : ""}`}>
            <AppScreenshotFrame src={screenshot.src} alt={screenshot.alt} hoverTilt />
            <figcaption className="flex flex-col gap-1">
              <MonoLabel className="text-marketing-accent">Fig. {index + 1}</MonoLabel>
              <span className="text-sm text-marketing-muted-fg">{screenshot.caption}</span>
            </figcaption>
          </figure>
        ))}
      </div>
    </SectionShell>
  );
}
