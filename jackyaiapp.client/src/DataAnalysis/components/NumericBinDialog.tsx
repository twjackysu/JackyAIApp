import DeleteIcon from '@mui/icons-material/Delete';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import { FieldGroupingDialogShell } from './FieldGroupingDialogShell';

import type { BinEntry } from '../types';

// ── Validation ────────────────────────────────────────────────────────────────

interface BinError {
  emptyLabel: boolean;
  invalidRange: boolean; // both bounds set, but min >= max
  overlapsWith: number[]; // indices of other bins that overlap THIS one
}

function computeBinErrors(bins: BinEntry[]): BinError[] {
  const parsed = bins.map((b) => ({
    min: b.min === '' ? -Infinity : parseFloat(b.min),
    max: b.max === '' ? +Infinity : parseFloat(b.max),
  }));

  return bins.map((b, i) => {
    const p = parsed[i];

    const invalidRange =
      b.min !== '' && b.max !== '' && !isNaN(+b.min) && !isNaN(+b.max) && +b.min >= +b.max;

    // Two [a, b) intervals overlap when a.max > b.min AND b.max > a.min
    const overlapsWith: number[] = [];
    if (!invalidRange) {
      for (let j = 0; j < bins.length; j++) {
        if (j === i) continue;
        const q = parsed[j];
        if (p.max > q.min && q.max > p.min) {
          overlapsWith.push(j);
        }
      }
    }

    return {
      emptyLabel: !b.label.trim(),
      invalidRange,
      overlapsWith,
    };
  });
}

// ── Condition preview ─────────────────────────────────────────────────────────

/** Human-readable interval condition, e.g. "18 ≤ x < 65" */
function conditionText(min: string, max: string): string {
  const hasMin = min !== '' && !isNaN(+min);
  const hasMax = max !== '' && !isNaN(+max);
  if (hasMin && hasMax) return `${+min} ≤ x < ${+max}`;
  if (hasMin && !hasMax) return `x ≥ ${+min}`;
  if (!hasMin && hasMax) return `x < ${+max}`;
  return '(all values — no bounds set)';
}

// ── Component ─────────────────────────────────────────────────────────────────

interface NumericBinDialogProps {
  open: boolean;
  onClose: () => void;
  onSave: (bins: BinEntry[], fallback: string) => void;
  fieldName: string;
  initialBins: BinEntry[];
  initialFallback: string;
}

export function NumericBinDialog({
  open,
  onClose,
  onSave,
  fieldName,
  initialBins,
  initialFallback,
}: NumericBinDialogProps) {
  const [bins, setBins] = useState<BinEntry[]>([]);
  const [fallback, setFallback] = useState('Other');

  useEffect(() => {
    if (open) {
      setBins(initialBins.map((b) => ({ ...b })));
      setFallback(initialFallback || 'Other');
    }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const addBin = () =>
    setBins((prev) => [...prev, { id: Date.now(), min: '', max: '', label: '' }]);
  const removeBin = (id: number) => setBins((prev) => prev.filter((b) => b.id !== id));
  const updateBin = (id: number, patch: Partial<BinEntry>) =>
    setBins((prev) => prev.map((b) => (b.id === id ? { ...b, ...patch } : b)));

  // Live validation — recomputed every render (bins are small)
  const errors = computeBinErrors(bins);
  const hasErrors = errors.some((e) => e.emptyLabel || e.invalidRange || e.overlapsWith.length > 0);

  // Collect global overlap messages (deduplicated by pair)
  const overlapPairs: string[] = [];
  const seen = new Set<string>();
  errors.forEach((e, i) => {
    e.overlapsWith.forEach((j) => {
      const key = [Math.min(i, j), Math.max(i, j)].join('-');
      if (!seen.has(key)) {
        seen.add(key);
        overlapPairs.push(`區間 ${i + 1} 與區間 ${j + 1} 重疊`);
      }
    });
  });

  const handleApply = () => {
    if (!hasErrors) onSave(bins, fallback);
  };

  return (
    <FieldGroupingDialogShell
      open={open}
      onClose={onClose}
      title="數值分組 (Numeric Bins)"
      fieldName={fieldName}
      hint={
        <>
          區間為<strong>左閉右開</strong>：當 <code>Min ≤ 值 &lt; Max</code>{' '}
          時，該筆資料屬於此區間。Min 留空代表 <em>−∞</em>（無下限）；Max 留空代表 <em>+∞</em>
          （無上限）。
        </>
      }
      addLabel="新增區間"
      onAdd={addBin}
      belowAdd={
        /* ── Global overlap errors ── */
        overlapPairs.length > 0 && (
          <Alert severity="error" sx={{ mt: 2 }}>
            <strong>偵測到重疊區間：</strong>
            <ul style={{ margin: '4px 0 0', paddingLeft: 16 }}>
              {overlapPairs.map((msg) => (
                <li key={msg}>{msg}</li>
              ))}
            </ul>
          </Alert>
        )
      }
      fallbackLabel="預設標籤（未落入任何區間的資料）"
      fallback={fallback}
      onFallbackChange={setFallback}
      hasErrors={hasErrors}
      onApply={handleApply}
    >
      {bins.length === 0 && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          尚無區間 — 點擊 <strong>新增區間</strong> 開始。
        </Typography>
      )}

      {bins.map((b, i) => {
        const e = errors[i];
        return (
          <Box key={b.id} sx={{ mb: 1.5 }}>
            {/* ── Input row ── */}
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ minWidth: 20, textAlign: 'right', flexShrink: 0 }}
              >
                {i + 1}.
              </Typography>

              <TextField
                size="small"
                label="最小值"
                placeholder="−∞"
                value={b.min}
                sx={{ flex: 1 }}
                error={e.invalidRange}
                onChange={(ev) => updateBin(b.id, { min: ev.target.value })}
                slotProps={{
                  htmlInput: { type: 'number' },
                }}
              />

              <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
                ≤ x &lt;
              </Typography>

              <TextField
                size="small"
                label="最大值"
                placeholder="+∞"
                value={b.max}
                sx={{ flex: 1 }}
                error={e.invalidRange}
                onChange={(ev) => updateBin(b.id, { max: ev.target.value })}
                slotProps={{
                  htmlInput: { type: 'number' },
                }}
              />

              <TextField
                size="small"
                label="標籤 *"
                value={b.label}
                sx={{ flex: 1.2 }}
                error={e.emptyLabel}
                onChange={(ev) => updateBin(b.id, { label: ev.target.value })}
              />

              <IconButton size="small" onClick={() => removeBin(b.id)}>
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Box>
            {/* ── Condition preview + per-bin errors ── */}
            <Box
              sx={{
                pl: '28px',
                mt: 0.25,
                display: 'flex',
                gap: 1,
                flexWrap: 'wrap',
                alignItems: 'center',
              }}
            >
              <Typography
                variant="caption"
                sx={{
                  fontFamily: 'monospace',
                  color: 'text.disabled',
                  bgcolor: 'action.hover',
                  px: 0.75,
                  borderRadius: '3px',
                }}
              >
                {conditionText(b.min, b.max)}
              </Typography>
              {e.emptyLabel && (
                <Typography variant="caption" color="error">
                  標籤為必填。
                </Typography>
              )}
              {e.invalidRange && (
                <Typography variant="caption" color="error">
                  最小值必須小於最大值。
                </Typography>
              )}
            </Box>
          </Box>
        );
      })}
    </FieldGroupingDialogShell>
  );
}
