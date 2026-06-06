import AddIcon from '@mui/icons-material/Add';
import FilterAltIcon from '@mui/icons-material/FilterAlt';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';

import { toFilterDtos } from '../utils/groupField';

import { FilterRow } from './FilterRow';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef } from '../types';

interface FilterEditorProps {
  filters: ActiveFilter[];
  fields: FieldDefinition[];
  transforms?: FieldTransformDef[]; // optional; Details tab passes nothing
  onChange: (filters: ActiveFilter[]) => void;
  sourceId?: string;
  tableName?: string;
}

export function FilterEditor({
  filters,
  fields,
  transforms = [],
  onChange,
  sourceId,
  tableName,
}: FilterEditorProps) {
  const addFilter = () =>
    onChange([
      ...filters,
      { id: Date.now(), fieldName: '', values: [], useTransform: false, transformId: null },
    ]);
  const removeFilter = (id: number) => onChange(filters.filter((f) => f.id !== id));
  const updateFilter = (updated: ActiveFilter) =>
    onChange(filters.map((f) => (f.id === updated.id ? updated : f)));

  const activeCount = toFilterDtos(filters, transforms).length;

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <FilterAltIcon fontSize="small" color="action" />
          <Typography variant="subtitle2">
            Filters
            {activeCount > 0 && (
              <Chip label={activeCount} size="small" color="primary" sx={{ ml: 1 }} />
            )}
          </Typography>
        </Box>
        <IconButton size="small" onClick={addFilter} title="Add filter">
          <AddIcon fontSize="small" />
        </IconButton>
      </Box>

      {filters.length === 0 && (
        <Typography variant="caption" color="text.secondary">
          No filters — click <strong>+</strong> to add one.
        </Typography>
      )}

      {filters.map((f) => (
        <FilterRow
          key={f.id}
          filter={f}
          fields={fields}
          transforms={transforms}
          onUpdate={updateFilter}
          onRemove={() => removeFilter(f.id)}
          sourceId={sourceId}
          tableName={tableName}
        />
      ))}
    </Box>
  );
}
