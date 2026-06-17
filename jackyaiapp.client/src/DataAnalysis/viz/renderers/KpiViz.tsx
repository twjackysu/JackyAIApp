import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';

const num = (v: unknown): number => (typeof v === 'number' ? v : parseFloat(String(v ?? '')) || 0);

/** Compact number: 1.2B / 3.4M / 56.7K / 890. */
function formatBig(n: number): string {
  const abs = Math.abs(n);
  if (abs >= 1e9) return `${(n / 1e9).toFixed(2)}B`;
  if (abs >= 1e6) return `${(n / 1e6).toFixed(2)}M`;
  if (abs >= 1e3) return `${(n / 1e3).toFixed(1)}K`;
  return n.toLocaleString(undefined, { maximumFractionDigits: 2 });
}

/** "2024-03" → "2023-03", "2024Q1" → "2023Q1", "2024" → "2023". */
function priorYearLabel(label: string): string | null {
  const m = label.match(/^(\d{4})(.*)$/);
  if (!m) return null;
  return `${Number(m[1]) - 1}${m[2]}`;
}

interface KpiVizProps {
  rows: Record<string, unknown>[];
  measureKeys: string[];
  temporalKey?: string;
}

/**
 * Single headline number. With a temporal dimension it shows the latest period's
 * value plus a change badge — year-over-year when the prior-year period exists,
 * otherwise vs the previous period.
 */
export function KpiViz({ rows, measureKeys, temporalKey }: KpiVizProps) {
  const measure = measureKeys[0];
  if (!measure || rows.length === 0) return <Alert severity="info">No measure to show.</Alert>;

  let value: number;
  let periodLabel: string | undefined;
  let compare: { abs: number; pct: number | null; kind: 'YoY' | '前期' } | null = null;

  if (temporalKey) {
    const sorted = [...rows].sort((a, b) =>
      String(a[temporalKey] ?? '').localeCompare(String(b[temporalKey] ?? '')),
    );
    const latest = sorted[sorted.length - 1];
    value = num(latest[measure]);
    periodLabel = String(latest[temporalKey] ?? '');

    const byLabel = new Map(sorted.map((r) => [String(r[temporalKey] ?? ''), num(r[measure])]));
    const py = priorYearLabel(periodLabel);
    let prev: number | undefined;
    let kind: 'YoY' | '前期' = '前期';
    if (py && byLabel.has(py)) {
      prev = byLabel.get(py);
      kind = 'YoY';
    } else if (sorted.length >= 2) {
      prev = num(sorted[sorted.length - 2][measure]);
    }
    if (prev !== undefined) {
      const abs = value - prev;
      compare = { abs, pct: prev !== 0 ? (abs / Math.abs(prev)) * 100 : null, kind };
    }
  } else {
    value = num(rows[0][measure]);
  }

  const up = (compare?.abs ?? 0) >= 0;
  const deltaColor = up ? 'success.main' : 'error.main';

  return (
    <Box
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        textAlign: 'center',
        gap: 0.5,
        p: 2,
      }}
    >
      <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 1 }}>
        {measure}
      </Typography>
      <Typography variant="h3" sx={{ fontWeight: 800, lineHeight: 1.1 }}>
        {formatBig(value)}
      </Typography>
      {periodLabel && (
        <Typography variant="caption" color="text.secondary">
          {periodLabel}
        </Typography>
      )}
      {compare && (
        <Chip
          size="small"
          icon={up ? <ArrowUpwardIcon fontSize="small" /> : <ArrowDownwardIcon fontSize="small" />}
          label={`${compare.pct === null ? formatBig(compare.abs) : `${up ? '+' : ''}${compare.pct.toFixed(1)}%`} ${compare.kind}`}
          sx={{
            mt: 0.5,
            color: deltaColor,
            borderColor: deltaColor,
            '& .MuiChip-icon': { color: deltaColor },
          }}
          variant="outlined"
        />
      )}
    </Box>
  );
}
