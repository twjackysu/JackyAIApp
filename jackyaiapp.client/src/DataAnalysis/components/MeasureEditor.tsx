import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Typography from '@mui/material/Typography';

import { AGGREGATION_TYPES } from '../constants';

import { FieldSelect } from './FieldSelect';

import type {
  AggregationType,
  FieldDefinition,
  MeasureFieldDto,
} from '../../apis/dataAnalysisApis/types';

import { ActionTooltip } from '@/components/hints';

interface MeasureEditorProps {
  measures: MeasureFieldDto[];
  fields: FieldDefinition[];
  onAdd: () => void;
  onRemove: (i: number) => void;
  onUpdate: (i: number, patch: Partial<MeasureFieldDto>) => void;
}

export function MeasureEditor({ measures, fields, onAdd, onRemove, onUpdate }: MeasureEditorProps) {
  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="subtitle2">量測欄位 (Measures)</Typography>
        <ActionTooltip title="新增量測欄位">
          <IconButton size="small" onClick={onAdd}>
            <AddIcon fontSize="small" />
          </IconButton>
        </ActionTooltip>
      </Box>

      {measures.map((m, i) => (
        <Box key={i} sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'center' }}>
          <FieldSelect
            fields={fields}
            value={m.fieldName}
            onChange={(fieldName) => onUpdate(i, { fieldName })}
            sx={{ flex: 2 }}
          />

          <FormControl size="small" sx={{ flex: 1 }}>
            <InputLabel>聚合方式</InputLabel>
            <Select
              value={m.aggregation}
              label="聚合方式"
              onChange={(e) => onUpdate(i, { aggregation: e.target.value as AggregationType })}
            >
              {AGGREGATION_TYPES.map((a) => (
                <MenuItem key={a} value={a}>
                  {a}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <IconButton size="small" onClick={() => onRemove(i)} disabled={measures.length === 1}>
            <DeleteIcon fontSize="small" />
          </IconButton>
        </Box>
      ))}
    </Box>
  );
}
