import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { SxProps, Theme } from '@mui/material/styles';

interface FieldSelectProps {
  /** Columns of the active table, in the order the schema returned them. */
  fields: FieldDefinition[];
  /** Currently picked column name; empty string when nothing is picked yet. */
  value: string;
  onChange: (fieldName: string) => void;
  /** Layout of the wrapping `FormControl` — the editors size this column differently. */
  sx?: SxProps<Theme>;
}

/**
 * The 「欄位」 dropdown shared by every DataAnalysis query editor (view-by rows, measures,
 * filters, the transform form). They all render the same list of column names, so the markup
 * lives here instead of being repeated — and the label, size and option rendering only have to
 * change in one place.
 */
export function FieldSelect({
  fields,
  value,
  onChange,
  sx = { flex: '0 0 160px' },
}: FieldSelectProps) {
  return (
    <FormControl size="small" sx={sx}>
      <InputLabel>欄位</InputLabel>
      <Select value={value} label="欄位" onChange={(e) => onChange(e.target.value as string)}>
        {fields.map((f) => (
          <MenuItem key={f.name} value={f.name}>
            {f.name}
          </MenuItem>
        ))}
      </Select>
    </FormControl>
  );
}
