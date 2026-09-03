import { describe, expect, it } from "vitest";
import { toCsv } from "./toCsv";

describe("toCsv", () => {
  it("joins headers and rows with CRLF, comma-separated", () => {
    const csv = toCsv(["Name", "Amount"], [["Acme", "100"]]);
    expect(csv).toBe("Name,Amount\r\nAcme,100\r\n");
  });

  it("quotes a field containing a comma", () => {
    const csv = toCsv(["Name"], [["Acme, Inc."]]);
    expect(csv.split("\r\n")[1]).toBe('"Acme, Inc."');
  });

  it("escapes an internal double quote by doubling it, then wraps in quotes", () => {
    const csv = toCsv(["Name"], [['Say "hi"']]);
    expect(csv.split("\r\n")[1]).toBe('"Say ""hi"""');
  });

  it("prefixes a leading = with a single quote to prevent formula injection in Excel", () => {
    const csv = toCsv(["Name"], [["=SUM(A1:A9)"]]);
    expect(csv.split("\r\n")[1]).toBe("'=SUM(A1:A9)");
  });

  it("prefixes a leading @, +, or - the same way", () => {
    expect(toCsv(["N"], [["@cmd"]]).split("\r\n")[1]).toBe("'@cmd");
    expect(toCsv(["N"], [["+cmd"]]).split("\r\n")[1]).toBe("'+cmd");
    expect(toCsv(["N"], [["-cmd"]]).split("\r\n")[1]).toBe("'-cmd");
  });
});
