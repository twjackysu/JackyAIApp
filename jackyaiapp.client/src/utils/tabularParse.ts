/**
 * Generic CSV/TSV parsing shared by any feature that needs to turn pasted or uploaded tabular
 * text into header-keyed rows (originally written for `DataAnalysis`'s upload dialog; extracted
 * so `Portfolio`'s CSV import can reuse it instead of a second, drift-prone implementation).
 */

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
