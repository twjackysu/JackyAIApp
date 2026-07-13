import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

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
 * Single headline number for the chosen measure (a dropdown switches measure
 * when there's more than one). Behaviour by shape:
 *  - time series      → latest period's value + change badge (YoY when the
 *                       prior-year period exists, else vs the previous period)
 *  - single row       → that value
 *  - grouped (non-time) → total of the measure across all rows
 */
export function KpiViz({ rows, measureKeys, temporalKey }: KpiVizProps) {
  // Selected measure index. Resets to the first measure on a new result because
  // the result view is remounted (keyed per result by SummarizeTab) — no effect
  // or in-render reset needed. Clamped in case the measure count shrinks.
  const [sel, setSel] = useState(0);
  const measure = measureKeys[Math.min(sel, measureKeys.length - 1)];
  if (!measure || rows.length === 0) return <Alert severity="info">沒有可顯示的量測欄位。</Alert>;

  let value: number;
  let subLabel: string | undefined;
  let compare: { abs: number; pct: number | null; kind: 'YoY' | '前期' } | null = null;

  if (temporalKey) {
    const sorted = [...rows].sort((a, b) =>
      String(a[temporalKey] ?? '').localeCompare(String(b[temporalKey] ?? '')),
    );
    const latest = sorted[sorted.length - 1];
    value = num(latest[measure]);
    subLabel = String(latest[temporalKey] ?? '');

    const byLabel = new Map(sorted.map((r) => [String(r[temporalKey] ?? ''), num(r[measure])]));
    const py = priorYearLabel(subLabel);
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
  } else if (rows.length === 1) {
    value = num(rows[0][measure]);
  } else {
    // Grouped, non-temporal → headline = total across all categories.
    value = rows.reduce((s, r) => s + num(r[measure]), 0);
    subLabel = `合計（${rows.length} 項）`;
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
      {measureKeys.length > 1 ? (
        <Select
          value={String(Math.min(sel, measureKeys.length - 1))}
          onChange={(e) => setSel(Number(e.target.value))}
          variant="standard"
          disableUnderline
          sx={{
            '& .MuiSelect-select': {
              py: 0,
              fontSize: '0.75rem',
              letterSpacing: 1,
              color: 'text.secondary',
              textTransform: 'uppercase',
            },
          }}
        >
          {measureKeys.map((m, i) => (
            <MenuItem key={m} value={String(i)} sx={{ fontSize: '0.85rem' }}>
              {m}
            </MenuItem>
          ))}
        </Select>
      ) : (
        <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 1 }}>
          {measure}
        </Typography>
      )}

      <Typography variant="h3" sx={{ fontWeight: 800, lineHeight: 1.1 }}>
        {formatBig(value)}
      </Typography>

      {subLabel && (
        <Typography variant="caption" color="text.secondary">
          {subLabel}
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
