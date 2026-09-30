import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Parses every `:root { ... }` and `.dark { ... }` block in globals.css (including the ones
// nested in @layer base — the regex only needs the innermost brace pair) into light/dark
// token maps. Dark inherits any token it does not override, matching CSS cascade.
function readTokens() {
  const css = readFileSync(path.resolve(process.cwd(), "src/app/globals.css"), "utf8");
  const light: Record<string, string> = {};
  const dark: Record<string, string> = {};
  const blockRe = /(:root|\.dark)\s*\{([^}]*)\}/g;
  for (const match of Array.from(css.matchAll(blockRe))) {
    const target = match[1] === ":root" ? light : dark;
    for (const decl of Array.from(match[2].matchAll(/--([\w-]+)\s*:\s*([^;]+);/g))) {
      target[decl[1]] = decl[2].trim();
    }
  }
  return { light, dark: { ...light, ...dark } };
}

function hslToRgb(value: string): [number, number, number] {
  const m = value.match(/^([\d.]+)\s+([\d.]+)%\s+([\d.]+)%$/);
  if (!m) throw new Error(`Not an HSL triple: "${value}"`);
  const h = Number(m[1]);
  const s = Number(m[2]) / 100;
  const l = Number(m[3]) / 100;
  const k = (n: number) => (n + h / 30) % 12;
  const a = s * Math.min(l, 1 - l);
  const f = (n: number) => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  return [f(0), f(8), f(4)];
}

function luminance([r, g, b]: [number, number, number]) {
  const c = (v: number) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
  return 0.2126 * c(r) + 0.7152 * c(g) + 0.0722 * c(b);
}

function ratio(a: string, b: string) {
  const [hi, lo] = [luminance(hslToRgb(a)), luminance(hslToRgb(b))].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const TEXT_PAIRS: [string, string][] = [
  ["foreground", "background"],
  ["foreground", "card"],
  ["muted-foreground", "background"],
  ["muted-foreground", "card"],
  ["muted-foreground", "surface-sunken"],
  ["status-delivered", "card"],
  ["destructive", "card"],
  ["sidebar-foreground", "sidebar-background"],
  ["sidebar-foreground", "sidebar-accent"],
  ["sidebar-accent-foreground", "sidebar-accent"],
  ["sidebar-muted", "sidebar-background"],
];
const NON_TEXT_PAIRS: [string, string][] = [["sidebar-primary", "sidebar-background"]];

describe("design tokens contrast", () => {
  const tokens = readTokens();
  for (const mode of ["light", "dark"] as const) {
    for (const [fg, bg] of TEXT_PAIRS) {
      it(`${mode}: --${fg} on --${bg} clears 4.5:1`, () => {
        expect(ratio(tokens[mode][fg], tokens[mode][bg])).toBeGreaterThanOrEqual(4.5);
      });
    }
    for (const [fg, bg] of NON_TEXT_PAIRS) {
      it(`${mode}: --${fg} on --${bg} clears 3:1`, () => {
        expect(ratio(tokens[mode][fg], tokens[mode][bg])).toBeGreaterThanOrEqual(3);
      });
    }
  }
  it("card differs from page background in light mode", () => {
    expect(tokens.light.card).not.toBe(tokens.light.background);
  });
});
