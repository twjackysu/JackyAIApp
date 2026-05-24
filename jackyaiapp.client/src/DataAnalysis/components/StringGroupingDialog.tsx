import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import OutlinedInput from '@mui/material/OutlinedInput';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import { useGetDistinctValuesQuery } from '../../apis/dataAnalysisApis';
import type { GroupEntry } from '../types';

interface StringGroupingDialogProps {
  open: boolean;
  onClose: () => void;
  onSave: (groups: GroupEntry[], fallback: string) => void;
  fieldName: string;
  initialGroups: GroupEntry[];
  initialFallback: string;
  sourceId?: string;
  tableName?: string;
}

export function StringGroupingDialog({
  open, onClose, onSave, fieldName, initialGroups, initialFallback, sourceId, tableName,
}: StringGroupingDialogProps) {
  const [groups, setGroups]     = useState<GroupEntry[]>([]);
  const [fallback, setFallback] = useState('Other');

  useEffect(() => {
    if (open) {
      setGroups(initialGroups.map((g) => ({ ...g, values: [...g.values] })));
      setFallback(initialFallback || 'Other');
    }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const { data: lvData, isFetching } = useGetDistinctValuesQuery(
    { field: fieldName, sourceId, tableName },
    { skip: !fieldName || !open },
  );
  const allValues = lvData?.data?.values ?? [];

  const addGroup    = () => setGroups((prev) => [...prev, { id: Date.now(), label: '', values: [] }]);
  const removeGroup = (id: number) => setGroups((prev) => prev.filter((g) => g.id !== id));
  const updateGroup = (id: number, patch: Partial<GroupEntry>) =>
    setGroups((prev) => prev.map((g) => (g.id === id ? { ...g, ...patch } : g)));

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>
        String Groups —{' '}
        <Box
          component="code"
          sx={{ bgcolor: 'action.selected', px: 0.5, borderRadius: '3px', fontFamily: 'monospace' }}
        >
          {fieldName}
        </Box>
      </DialogTitle>

      <DialogContent dividers>
        <Typography variant="caption" color="text.secondary" sx={{ mb: 2, display: 'block' }}>
          Map raw values into logical groups. Unmatched rows fall back to the Fallback label.
        </Typography>

        {groups.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            No groups yet — click <strong>Add Group</strong> to start.
          </Typography>
        )}

        {groups.map((g) => (
          <Box key={g.id} sx={{ display: 'flex', gap: 1, mb: 1.5, alignItems: 'flex-start' }}>
            <TextField
              size="small" label="Group label" value={g.label}
              sx={{ flex: '0 0 150px' }}
              onChange={(e) => updateGroup(g.id, { label: e.target.value })}
            />
            <FormControl size="small" sx={{ flex: 1 }}>
              <InputLabel>{isFetching ? 'Loading…' : 'Values'}</InputLabel>
              <Select
                multiple value={g.values} disabled={isFetching}
                onChange={(e) =>
                  updateGroup(g.id, {
                    values: typeof e.target.value === 'string'
                      ? e.target.value.split(',')
                      : (e.target.value as string[]),
                  })
                }
                input={<OutlinedInput label={isFetching ? 'Loading…' : 'Values'} />}
                renderValue={(sel) => (
                  <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                    {sel.map((v) => <Chip key={v} label={v} size="small" />)}
                  </Box>
                )}
              >
                {isFetching
                  ? <MenuItem disabled><em>Loading…</em></MenuItem>
                  : allValues.length === 0
                    ? <MenuItem disabled><em>No values found</em></MenuItem>
                    : allValues.map((v) => <MenuItem key={v} value={v}>{v}</MenuItem>)
                }
              </Select>
            </FormControl>
            <IconButton size="small" onClick={() => removeGroup(g.id)} sx={{ mt: 0.5 }}>
              <DeleteIcon fontSize="small" />
            </IconButton>
          </Box>
        ))}

        <Button size="small" startIcon={<AddIcon />} onClick={addGroup} sx={{ mt: 1 }}>
          Add Group
        </Button>

        <Divider sx={{ my: 2 }} />

        <TextField
          size="small" fullWidth
          label="Fallback label (values matching no group)"
          value={fallback}
          onChange={(e) => setFallback(e.target.value)}
        />
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={() => onSave(groups, fallback)}>Apply</Button>
      </DialogActions>
    </Dialog>
  );
}
