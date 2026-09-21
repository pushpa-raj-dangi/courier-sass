import { describe, expect, it } from "vitest";
import en from "../../messages/en.json";
import ne from "../../messages/ne.json";

function flatten(obj: Record<string, unknown>, prefix = ""): Record<string, string> {
  return Object.entries(obj).reduce<Record<string, string>>((acc, [key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === "string") {
      acc[path] = value;
    } else {
      Object.assign(acc, flatten(value as Record<string, unknown>, path));
    }
    return acc;
  }, {});
}

describe("message catalogs", () => {
  const enFlat = flatten(en);
  const neFlat = flatten(ne);

  it("have identical key sets, so no key can be missing in Nepali", () => {
    expect(Object.keys(neFlat).sort()).toEqual(Object.keys(enFlat).sort());
  });

  it("have no empty strings", () => {
    for (const [key, value] of [...Object.entries(enFlat), ...Object.entries(neFlat)]) {
      expect(value.trim(), key).not.toBe("");
    }
  });

  it("use the same ICU placeholders in both languages", () => {
    const placeholders = (s: string) => (s.match(/\{[^}]+\}/g) ?? []).sort();
    for (const key of Object.keys(enFlat)) {
      expect(placeholders(neFlat[key]), key).toEqual(placeholders(enFlat[key]));
    }
  });
});
