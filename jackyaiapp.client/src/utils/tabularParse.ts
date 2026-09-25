/** Guess whether a text blob is tab- or comma-separated, by comparing counts on the first line. */
export function detectSep(text: string): string {
  const firstLine = text.split('\n')[0] ?? '';
  const tabs = (firstLine.match(/\t/g) ?? []).length;
  const commas = (firstLine.match(/,/g) ?? []).length;
  return tabs >= commas ? '\t' : ',';
}

/** Splits one line into cells for the given separator. Comma-separated lines get quote-aware
 * parsing (embedded commas/quotes inside "..."); tab-separated lines are split as-is. */
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

/**
 * Parses a header-first CSV/TSV blob into header-keyed row objects. Auto-detects comma vs. tab
 * separator from the first line. Returns null when there's no header plus at least one data row,
 * or fewer than 2 columns were detected.
 */
export function parseTabular(
  text: string,
): { headers: string[]; rows: Record<string, string>[] } | null {
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
