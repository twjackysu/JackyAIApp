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

import type {
  AggregationType,
  FieldDefinition,
  MeasureFieldDto,
} from '../../apis/dataAnalysisApis/types';

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
        <Typography variant="subtitle2">Measures</Typography>
        <IconButton size="small" onClick={onAdd} title="Add measure">
          <AddIcon fontSize="small" />
        </IconButton>
      </Box>

      {measures.map((m, i) => (
        <Box key={i} sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'center' }}>
          <FormControl size="small" sx={{ flex: 2 }}>
            <InputLabel>Field</InputLabel>
            <Select
              value={m.fieldName}
              label="Field"
              onChange={(e) => onUpdate(i, { fieldName: e.target.value as string })}
            >
              {fields.map((f) => (
                <MenuItem key={f.name} value={f.name}>
                  {f.name}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <FormControl size="small" sx={{ flex: 1 }}>
            <InputLabel>Aggregation</InputLabel>
            <Select
              value={m.aggregation}
              label="Aggregation"
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
