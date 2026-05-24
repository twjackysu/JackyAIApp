import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
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
          Intervals are left-closed, right-open [Min, Max). Leave a bound empty for an open end.
        </Typography>

        {bins.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            No bins yet — click <strong>Add Bin</strong> to start.
          </Typography>
        )}

        {bins.map((b, i) => (
          <Box key={b.id} sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'center' }}>
            <Typography variant="caption" color="text.secondary" sx={{ minWidth: 18, textAlign: 'right' }}>
              {i + 1}.
            </Typography>
            <TextField
              size="small" label="Min (incl.)" placeholder="open"
              value={b.min} inputProps={{ type: 'number' }} sx={{ flex: 1 }}
              onChange={(e) => updateBin(b.id, { min: e.target.value })}
            />
            <Typography variant="body2" color="text.secondary">–</Typography>
            <TextField
              size="small" label="Max (excl.)" placeholder="open"
              value={b.max} inputProps={{ type: 'number' }} sx={{ flex: 1 }}
              onChange={(e) => updateBin(b.id, { max: e.target.value })}
            />
            <TextField
              size="small" label="Label" value={b.label} sx={{ flex: 1 }}
              onChange={(e) => updateBin(b.id, { label: e.target.value })}
            />
            <IconButton size="small" onClick={() => removeBin(b.id)}>
              <DeleteIcon fontSize="small" />
            </IconButton>
          </Box>
        ))}

        <Button size="small" startIcon={<AddIcon />} onClick={addBin} sx={{ mt: 1 }}>
          Add Bin
        </Button>

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
        <Button variant="contained" onClick={() => onSave(bins, fallback)}>Apply</Button>
      </DialogActions>
    </Dialog>
  );
}
