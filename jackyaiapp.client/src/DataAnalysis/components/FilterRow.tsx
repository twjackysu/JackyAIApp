import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import OutlinedInput from '@mui/material/OutlinedInput';
import Select from '@mui/material/Select';

import { useGetDistinctValuesQuery } from '../../apis/dataAnalysisApis';
import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter } from '../types';

interface FilterRowProps {
  filter: ActiveFilter;
  fields: FieldDefinition[];
  onUpdate: (f: ActiveFilter) => void;
  onRemove: () => void;
  sourceId?: string;
  tableName?: string;
}

export function FilterRow({
  filter, fields, onUpdate, onRemove, sourceId, tableName,
}: FilterRowProps) {
  const { data: lvData, isFetching } = useGetDistinctValuesQuery(
    { field: filter.fieldName, sourceId, tableName },
    { skip: !filter.fieldName },
  );
  const distinctValues = lvData?.data?.values ?? [];

  const valuesLabel = isFetching ? 'Loading…' : filter.fieldName ? 'Values' : 'Select a field first';

  return (
    <Box sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'flex-start' }}>
      <FormControl size="small" sx={{ flex: '0 0 160px' }}>
        <InputLabel>Field</InputLabel>
        <Select
          value={filter.fieldName}
          label="Field"
          onChange={(e) =>
            onUpdate({ ...filter, fieldName: e.target.value as string, values: [] })
          }
        >
          {fields.map((f) => (
            <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
          ))}
        </Select>
      </FormControl>

      <FormControl size="small" sx={{ flex: 1 }}>
        <InputLabel>{valuesLabel}</InputLabel>
        <Select
          multiple
          value={filter.values}
          disabled={!filter.fieldName || isFetching}
          onChange={(e) =>
            onUpdate({
              ...filter,
              values: typeof e.target.value === 'string'
                ? e.target.value.split(',')
                : (e.target.value as string[]),
            })
          }
          input={<OutlinedInput label={valuesLabel} />}
          renderValue={(sel) => (
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {sel.map((v) => <Chip key={v} label={v} size="small" />)}
            </Box>
          )}
        >
          {isFetching
            ? <MenuItem disabled><em>Loading values…</em></MenuItem>
            : distinctValues.length === 0
              ? <MenuItem disabled><em>No values found</em></MenuItem>
              : distinctValues.map((v) => <MenuItem key={v} value={v}>{v}</MenuItem>)
          }
        </Select>
      </FormControl>

      <IconButton size="small" onClick={onRemove} title="Remove filter" sx={{ mt: 0.5 }}>
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
