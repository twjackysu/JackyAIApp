import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';

import { DATE_GRANULARITIES } from '../../constants';
import { FieldDialogTitle } from '../FieldDialogTitle';

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
      <FieldDialogTitle fieldName={target.fieldName}>編輯 DateTrunc</FieldDialogTitle>
      <DialogContent dividers sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 2 }}>
        <TextField
          size="small"
          fullWidth
          label="名稱"
          value={target.name}
          onChange={(e) => onChange((t) => t && { ...t, name: e.target.value })}
        />
        <FormControl size="small" fullWidth>
          <InputLabel>時間粒度</InputLabel>
          <Select
            value={target.granularity}
            label="時間粒度"
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
        <Button onClick={onClose}>取消</Button>
        <Button variant="contained" onClick={onSave}>
          儲存
        </Button>
      </DialogActions>
    </Dialog>
  );
}
