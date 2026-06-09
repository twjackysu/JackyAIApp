import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';

import { DATE_GRANULARITIES } from '../../constants';

import type { DateGranularity } from '../../../apis/dataAnalysisApis/types';
import type { FieldTransformDef } from '../../types';

interface DateTruncEditDialogProps {
  target: FieldTransformDef;
  onChange: React.Dispatch<React.SetStateAction<FieldTransformDef | null>>;
  onClose: () => void;
  onSave: () => void;
}

export function DateTruncEditDialog({
  target,
  onChange,
  onClose,
  onSave,
}: DateTruncEditDialogProps) {
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>
        Edit DateTrunc —{' '}
        <Box
          component="code"
          sx={{
            bgcolor: 'action.selected',
            px: 0.5,
            borderRadius: '3px',
            fontFamily: 'monospace',
          }}
        >
          {target.fieldName}
        </Box>
      </DialogTitle>
      <DialogContent dividers sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 2 }}>
        <TextField
          size="small"
          fullWidth
          label="Name"
          value={target.name}
          onChange={(e) => onChange((t) => t && { ...t, name: e.target.value })}
        />
        <FormControl size="small" fullWidth>
          <InputLabel>Granularity</InputLabel>
          <Select
            value={target.granularity}
            label="Granularity"
            onChange={(e) =>
              onChange((t) => t && { ...t, granularity: e.target.value as DateGranularity })
            }
          >
            {DATE_GRANULARITIES.map((g) => (
              <MenuItem key={g} value={g}>
                {g}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" onClick={onSave}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
