// The package ships index.d.ts but its "exports" map has no "types" condition, so TypeScript's
// bundler resolution lands on ndc.js and reports TS7016. This mirrors the shipped typings.
declare module "@remotemerge/nepali-date-converter" {
  export default class DateConverter {
    constructor(dateInput: string);
    toAd(): { year: number; month: number; date: number; day: string };
    toBs(): { year: number; month: number; date: number; day: string };
  }
}
