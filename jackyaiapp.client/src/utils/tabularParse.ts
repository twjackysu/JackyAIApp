export interface TabularParseResult {
  headers: string[];
  rows: Record<string, string>[];
}

/** Comma vs. tab, guessed from whichever is more common on the first line. */
export function detectSep(text: string): string {
  const firstLine = text.split('\n')[0] ?? '';
  const tabs = (firstLine.match(/\t/g) ?? []).length;
  const commas = (firstLine.match(/,/g) ?? []).length;
  return tabs >= commas ? '\t' : ',';
}

export function parseLine(line: string, sep: string): string[] {
  if (sep !== ',') return line.split(sep).map((c) => c.trim());

  // Quoted CSV parsing
  const result: string[] = [];
  let current = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && line[i + 1] === '"') {
        current += '"';
        i++;
      } else inQuotes = !inQuotes;
    } else if (ch === ',' && !inQuotes) {
      result.push(current.trim());
      current = '';
    } else {
      current += ch;
    }
  }
  result.push(current.trim());
  return result;
}

/** Parses a header-row-keyed CSV/TSV (comma/tab auto-detected). Returns null when there's no
 * usable header row (fewer than 2 non-empty lines, or fewer than 2 columns). */
export function parseTabular(text: string): TabularParseResult | null {
  const lines = text
    .trim()
    .split('\n')
    .filter((l) => l.trim().length > 0);
  if (lines.length < 2) return null;

  const sep = detectSep(text);
  const headers = parseLine(lines[0], sep).map((h) => h.replace(/^"|"$/g, '').trim());
  if (headers.length < 2) return null;

  const rows = lines.slice(1).map((line) => {
    const cells = parseLine(line, sep);
    return Object.fromEntries(
      headers.map((h, i) => [h, (cells[i] ?? '').replace(/^"|"$/g, '').trim()]),
    );
  });

  return { headers, rows };
}
