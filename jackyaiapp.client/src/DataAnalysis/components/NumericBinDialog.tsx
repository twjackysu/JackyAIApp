import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import type { BinEntry } from '../types';

// ── Validation ────────────────────────────────────────────────────────────────

interface BinError {
  emptyLabel: boolean;
  invalidRange: boolean;  // both bounds set, but min >= max
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
      b.min !== '' && b.max !== '' &&
      !isNaN(+b.min) && !isNaN(+b.max) &&
      +b.min >= +b.max;

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
  if (hasMin && hasMax)  return `${+min} ≤ x < ${+max}`;
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
  open, onClose, onSave, fieldName, initialBins, initialFallback,
}: NumericBinDialogProps) {
  const [bins, setBins]         = useState<BinEntry[]>([]);
  const [fallback, setFallback] = useState('Other');

  useEffect(() => {
    if (open) {
      setBins(initialBins.map((b) => ({ ...b })));
      setFallback(initialFallback || 'Other');
    }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const addBin    = () => setBins((prev) => [...prev, { id: Date.now(), min: '', max: '', label: '' }]);
  const removeBin = (id: number) => setBins((prev) => prev.filter((b) => b.id !== id));
  const updateBin = (id: number, patch: Partial<BinEntry>) =>
    setBins((prev) => prev.map((b) => (b.id === id ? { ...b, ...patch } : b)));

  // Live validation — recomputed every render (bins are small)
  const errors    = computeBinErrors(bins);
  const hasErrors = errors.some(
    (e) => e.emptyLabel || e.invalidRange || e.overlapsWith.length > 0,
  );

  // Collect global overlap messages (deduplicated by pair)
  const overlapPairs: string[] = [];
  const seen = new Set<string>();
  errors.forEach((e, i) => {
    e.overlapsWith.forEach((j) => {
      const key = [Math.min(i, j), Math.max(i, j)].join('-');
      if (!seen.has(key)) {
        seen.add(key);
        overlapPairs.push(`Bin ${i + 1} and Bin ${j + 1} overlap`);
      }
    });
  });

  const handleApply = () => {
    if (!hasErrors) onSave(bins, fallback);
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>
        Numeric Bins —{' '}
        <Box
          component="code"
          sx={{ bgcolor: 'action.selected', px: 0.5, borderRadius: '3px', fontFamily: 'monospace' }}
        >
          {fieldName}
        </Box>
      </DialogTitle>

      <DialogContent dividers>
        <Typography variant="caption" color="text.secondary" sx={{ mb: 2, display: 'block' }}>
          Intervals are <strong>left-closed, right-open</strong>: a row matches bin [Min, Max) when{' '}
          <code>Min ≤ value &lt; Max</code>. Leave Min blank for <em>−∞</em> (no lower bound);
          leave Max blank for <em>+∞</em> (no upper bound).
        </Typography>

        {bins.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            No bins yet — click <strong>Add Bin</strong> to start.
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
                  size="small" label="Min" placeholder="−∞"
                  value={b.min}
                  inputProps={{ type: 'number' }}
                  sx={{ flex: 1 }}
                  error={e.invalidRange}
                  onChange={(ev) => updateBin(b.id, { min: ev.target.value })}
                />

                <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
                  ≤ x &lt;
                </Typography>

                <TextField
                  size="small" label="Max" placeholder="+∞"
                  value={b.max}
                  inputProps={{ type: 'number' }}
                  sx={{ flex: 1 }}
                  error={e.invalidRange}
                  onChange={(ev) => updateBin(b.id, { max: ev.target.value })}
                />

                <TextField
                  size="small" label="Label *" value={b.label}
                  sx={{ flex: 1.2 }}
                  error={e.emptyLabel}
                  onChange={(ev) => updateBin(b.id, { label: ev.target.value })}
                />

                <IconButton size="small" onClick={() => removeBin(b.id)}>
                  <DeleteIcon fontSize="small" />
                </IconButton>
              </Box>

              {/* ── Condition preview + per-bin errors ── */}
              <Box sx={{ pl: '28px', mt: 0.25, display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
                <Typography
                  variant="caption"
                  sx={{ fontFamily: 'monospace', color: 'text.disabled', bgcolor: 'action.hover', px: 0.75, borderRadius: '3px' }}
                >
                  {conditionText(b.min, b.max)}
                </Typography>
                {e.emptyLabel && (
                  <Typography variant="caption" color="error">Label is required.</Typography>
                )}
                {e.invalidRange && (
                  <Typography variant="caption" color="error">Min must be less than Max.</Typography>
                )}
              </Box>
            </Box>
          );
        })}

        <Button size="small" startIcon={<AddIcon />} onClick={addBin} sx={{ mt: 0.5 }}>
          Add Bin
        </Button>

        {/* ── Global overlap errors ── */}
        {overlapPairs.length > 0 && (
          <Alert severity="error" sx={{ mt: 2 }}>
            <strong>Overlapping intervals detected:</strong>
            <ul style={{ margin: '4px 0 0', paddingLeft: 16 }}>
              {overlapPairs.map((msg) => <li key={msg}>{msg}</li>)}
            </ul>
          </Alert>
        )}

        <Divider sx={{ my: 2 }} />

        <TextField
          size="small" fullWidth
          label="Fallback label (rows matching no bin)"
          value={fallback}
          onChange={(e) => setFallback(e.target.value)}
        />
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={hasErrors}
          onClick={handleApply}
          title={hasErrors ? 'Fix the errors above before applying' : undefined}
        >
          Apply{hasErrors ? ' (fix errors first)' : ''}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
