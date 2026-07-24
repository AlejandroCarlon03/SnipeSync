/**
 * Minimal .xlsx writer — enough to turn a table of cells into a real Excel
 * workbook, with no npm dependency.
 *
 * Why hand-rolled: the only thing the dashboard exports is one flat sheet, and
 * the SPA bundle is already large enough to trip Vite's 500KB warning. A full
 * spreadsheet library (exceljs/SheetJS) would add far more weight than the ~150
 * lines below. CSV was the other option, but Excel mangles it (leading zeros,
 * separator locale) and the ask was specifically Excel.
 *
 * An .xlsx is a ZIP of XML parts. Entries are written with the "store" method
 * (no compression) so we need no deflate implementation — Excel accepts that.
 */

export type CellValue = string | number | boolean | Date | null | undefined;

export interface SheetColumn {
  header: string;
  /** Column width in characters; defaults to a width derived from the data. */
  width?: number;
}

// ---------------------------------------------------------------------------
// XML parts
// ---------------------------------------------------------------------------

/** Escape text for XML, dropping control characters Excel rejects outright. */
function esc(value: string): string {
  return value
    // eslint-disable-next-line no-control-regex
    .replace(/[\x00-\x08\x0B\x0C\x0E-\x1F]/g, "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

/** 0 -> A, 25 -> Z, 26 -> AA. */
function colName(index: number): string {
  let name = "";
  let n = index;
  do {
    name = String.fromCharCode(65 + (n % 26)) + name;
    n = Math.floor(n / 26) - 1;
  } while (n >= 0);
  return name;
}

/** Excel serial date. 25569 = days between 1899-12-30 and the Unix epoch. */
function toSerialDate(d: Date): number {
  return d.getTime() / 86_400_000 + 25569;
}

const STYLE_DEFAULT = 0;
const STYLE_HEADER = 1;
const STYLE_DATETIME = 2;

function cellXml(ref: string, value: CellValue, style: number): string {
  if (value === null || value === undefined || value === "") {
    return style === STYLE_DEFAULT ? "" : `<c r="${ref}" s="${style}"/>`;
  }
  if (value instanceof Date) {
    return `<c r="${ref}" s="${STYLE_DATETIME}"><v>${toSerialDate(value)}</v></c>`;
  }
  if (typeof value === "number") {
    return `<c r="${ref}" s="${style}"><v>${Number.isFinite(value) ? value : 0}</v></c>`;
  }
  if (typeof value === "boolean") {
    return `<c r="${ref}" s="${style}" t="b"><v>${value ? 1 : 0}</v></c>`;
  }
  // Inline strings keep us from having to build a sharedStrings part.
  return `<c r="${ref}" s="${style}" t="inlineStr"><is><t xml:space="preserve">${esc(value)}</t></is></c>`;
}

function sheetXml(columns: SheetColumn[], rows: CellValue[][]): string {
  // Width from the widest value in the column, so nothing lands as "#####".
  const widths = columns.map((c, i) => {
    if (c.width) return c.width;
    let max = c.header.length;
    for (const row of rows) {
      const v = row[i];
      const len =
        v instanceof Date ? 19 : v === null || v === undefined ? 0 : String(v).length;
      if (len > max) max = len;
    }
    return Math.min(Math.max(max + 2, 10), 60);
  });

  const cols = widths
    .map((w, i) => `<col min="${i + 1}" max="${i + 1}" width="${w}" customWidth="1"/>`)
    .join("");

  const header =
    `<row r="1">` +
    columns.map((c, i) => cellXml(`${colName(i)}1`, c.header, STYLE_HEADER)).join("") +
    `</row>`;

  const body = rows
    .map((row, r) => {
      const n = r + 2;
      const cells = row
        .map((v, i) => cellXml(`${colName(i)}${n}`, v, STYLE_DEFAULT))
        .join("");
      return `<row r="${n}">${cells}</row>`;
    })
    .join("");

  const lastCol = colName(Math.max(columns.length - 1, 0));
  const dim = `A1:${lastCol}${rows.length + 1}`;

  return (
    `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
    `<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">` +
    `<dimension ref="${dim}"/>` +
    // Freeze the header row and give every column a filter dropdown — this is
    // the first thing anyone does to an exported table by hand anyway.
    `<sheetViews><sheetView workbookViewId="0" tabSelected="1">` +
    `<pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>` +
    `</sheetView></sheetViews>` +
    `<sheetFormatPr defaultRowHeight="15"/>` +
    `<cols>${cols}</cols>` +
    `<sheetData>${header}${body}</sheetData>` +
    `<autoFilter ref="${dim}"/>` +
    `</worksheet>`
  );
}

const STYLES_XML =
  `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
  `<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">` +
  `<numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy\\-mm\\-dd\\ hh:mm:ss"/></numFmts>` +
  `<fonts count="2">` +
  `<font><sz val="11"/><name val="Calibri"/></font>` +
  `<font><b/><sz val="11"/><name val="Calibri"/></font>` +
  `</fonts>` +
  `<fills count="2"><fill><patternFill patternType="none"/></fill>` +
  `<fill><patternFill patternType="gray125"/></fill></fills>` +
  `<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>` +
  `<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>` +
  // Order here defines STYLE_DEFAULT / STYLE_HEADER / STYLE_DATETIME above.
  `<cellXfs count="3">` +
  `<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>` +
  `<xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>` +
  `<xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>` +
  `</cellXfs>` +
  `<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>` +
  `</styleSheet>`;

const CONTENT_TYPES_XML =
  `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
  `<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">` +
  `<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>` +
  `<Default Extension="xml" ContentType="application/xml"/>` +
  `<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>` +
  `<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>` +
  `<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>` +
  `</Types>`;

const ROOT_RELS_XML =
  `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
  `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">` +
  `<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>` +
  `</Relationships>`;

const WORKBOOK_RELS_XML =
  `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
  `<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">` +
  `<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>` +
  `<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>` +
  `</Relationships>`;

function workbookXml(sheetName: string): string {
  return (
    `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
    `<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" ` +
    `xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">` +
    `<sheets><sheet name="${esc(sheetName)}" sheetId="1" r:id="rId1"/></sheets>` +
    `</workbook>`
  );
}

// ---------------------------------------------------------------------------
// ZIP container (store method only)
// ---------------------------------------------------------------------------

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let i = 0; i < 256; i++) {
    let c = i;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[i] = c >>> 0;
  }
  return table;
})();

function crc32(bytes: Uint8Array): number {
  let c = 0xffffffff;
  for (let i = 0; i < bytes.length; i++) c = CRC_TABLE[(c ^ bytes[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

interface ZipEntry {
  name: string;
  data: Uint8Array;
}

function zip(entries: ZipEntry[]): Blob {
  const encoder = new TextEncoder();
  const out: number[] = [];
  const push16 = (target: number[], v: number) => target.push(v & 0xff, (v >>> 8) & 0xff);
  const push32 = (target: number[], v: number) =>
    target.push(v & 0xff, (v >>> 8) & 0xff, (v >>> 16) & 0xff, (v >>> 24) & 0xff);

  const central: number[] = [];
  let offset = 0;

  for (const entry of entries) {
    const nameBytes = encoder.encode(entry.name);
    const crc = crc32(entry.data);
    const size = entry.data.length;

    // Local file header
    push32(out, 0x04034b50);
    push16(out, 20); // version needed
    push16(out, 0x0800); // flags: UTF-8 filenames
    push16(out, 0); // method: store
    push16(out, 0); // mod time
    push16(out, 0x2821); // mod date (2000-01-01; keeps output byte-stable)
    push32(out, crc);
    push32(out, size);
    push32(out, size);
    push16(out, nameBytes.length);
    push16(out, 0); // extra field length
    out.push(...nameBytes);
    for (let i = 0; i < size; i++) out.push(entry.data[i]);

    // Central directory record for the same entry
    push32(central, 0x02014b50);
    push16(central, 20); // version made by
    push16(central, 20); // version needed
    push16(central, 0x0800);
    push16(central, 0);
    push16(central, 0);
    push16(central, 0x2821);
    push32(central, crc);
    push32(central, size);
    push32(central, size);
    push16(central, nameBytes.length);
    push16(central, 0); // extra
    push16(central, 0); // comment
    push16(central, 0); // disk number
    push16(central, 0); // internal attrs
    push32(central, 0); // external attrs
    push32(central, offset);
    central.push(...nameBytes);

    offset = out.length;
  }

  const centralOffset = out.length;
  out.push(...central);

  // End of central directory
  push32(out, 0x06054b50);
  push16(out, 0);
  push16(out, 0);
  push16(out, entries.length);
  push16(out, entries.length);
  push32(out, central.length);
  push32(out, centralOffset);
  push16(out, 0); // comment length

  return new Blob([new Uint8Array(out)], {
    type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  });
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/** Build a single-sheet .xlsx workbook as a Blob. */
export function buildWorkbook(
  columns: SheetColumn[],
  rows: CellValue[][],
  sheetName = "Sheet1",
): Blob {
  const encoder = new TextEncoder();
  const part = (name: string, xml: string): ZipEntry => ({
    name,
    data: encoder.encode(xml),
  });

  return zip([
    part("[Content_Types].xml", CONTENT_TYPES_XML),
    part("_rels/.rels", ROOT_RELS_XML),
    part("xl/workbook.xml", workbookXml(sheetName)),
    part("xl/_rels/workbook.xml.rels", WORKBOOK_RELS_XML),
    part("xl/styles.xml", STYLES_XML),
    part("xl/worksheets/sheet1.xml", sheetXml(columns, rows)),
  ]);
}

/** Build the workbook and hand it to the browser as a download. */
export function downloadWorkbook(
  filename: string,
  columns: SheetColumn[],
  rows: CellValue[][],
  sheetName = "Sheet1",
): void {
  const url = URL.createObjectURL(buildWorkbook(columns, rows, sheetName));
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Revoke on the next tick; revoking synchronously can cancel the download.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
