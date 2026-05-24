import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import OutlinedInput from '@mui/material/OutlinedInput';
import Select from '@mui/material/Select';
import Switch from '@mui/material/Switch';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

import { useGetDistinctValuesQuery } from '../../apis/dataAnalysisApis';
import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef } from '../types';

interface FilterRowProps {
  filter:     ActiveFilter;
  fields:     FieldDefinition[];
  transforms: FieldTransformDef[];
  onUpdate:   (f: ActiveFilter) => void;
  onRemove:   () => void;
  sourceId?:  string;
  tableName?: string;
}

export function FilterRow({
  filter, fields, transforms, onUpdate, onRemove, sourceId, tableName,
}: FilterRowProps) {
  // Only StringGrouping transforms are supported in filters
  const stringTransforms = transforms.filter(
    (t) => t.fieldName === filter.fieldName && t.type === 'StringGrouping',
  );
  const hasStringTransform = stringTransforms.length > 0;

  // Resolve the active transform (if any)
  const activeTransform = filter.useTransform && filter.transformId !== null
    ? transforms.find((t) => t.id === filter.transformId)
    : undefined;

  // Fetch raw distinct values (skipped while in transform mode)
  const { data: lvData, isFetching } = useGetDistinctValuesQuery(
    { field: filter.fieldName, sourceId, tableName },
    { skip: !filter.fieldName || filter.useTransform },
  );
  const rawDistinctValues = lvData?.data?.values ?? [];

  // Values shown in the multi-select
  const displayValues: string[] = filter.useTransform && activeTransform?.type === 'StringGrouping'
    ? activeTransform.groups.map((g) => g.label)
    : rawDistinctValues;

  const valuesLabel = isFetching
    ? 'Loading…'
    : filter.fieldName
      ? filter.useTransform ? 'Group labels' : 'Values'
      : 'Select a field first';

  // ── Handlers ──────────────────────────────────────────────────────────────

  const handleFieldChange = (fieldName: string) => {
    // When field changes, reset values and transform state
    onUpdate({ ...filter, fieldName, values: [], useTransform: false, transformId: null });
  };

  const handleTransformToggle = (checked: boolean) => {
    if (checked) {
      // Auto-select first matching transform; clear stale values
      const first = stringTransforms[0];
      onUpdate({
        ...filter,
        useTransform: true,
        transformId:  first?.id ?? null,
        values:       [],
      });
    } else {
      onUpdate({ ...filter, useTransform: false, transformId: null, values: [] });
    }
  };

  const handleTransformSelect = (transformId: number) => {
    onUpdate({ ...filter, transformId, values: [] });
  };

  const handleValuesChange = (raw: string | string[]) => {
    const values = typeof raw === 'string' ? raw.split(',') : raw;
    onUpdate({ ...filter, values });
  };

  return (
    <Box sx={{ display: 'flex', gap: 1, mb: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {/* ── Field selector ── */}
      <FormControl size="small" sx={{ flex: '0 0 160px' }}>
        <InputLabel>Field</InputLabel>
        <Select
          value={filter.fieldName}
          label="Field"
          onChange={(e) => handleFieldChange(e.target.value as string)}
        >
          {fields.map((f) => (
            <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
          ))}
        </Select>
      </FormControl>

      {/* ── Transform controls (only when StringGrouping transform available) ── */}
      {filter.fieldName && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          {hasStringTransform ? (
            <>
              <Tooltip
                title="Filter by group labels (expands to raw values before querying)"
                placement="top"
              >
                <FormControlLabel
                  sx={{ m: 0 }}
                  control={
                    <Switch
                      size="small"
                      checked={filter.useTransform}
                      onChange={(e) => handleTransformToggle(e.target.checked)}
                    />
                  }
                  label={
                    <Typography variant="caption" color="text.secondary">
                      Use transform
                    </Typography>
                  }
                />
              </Tooltip>

              {/* When multiple transforms exist for the field, let user pick */}
              {filter.useTransform && stringTransforms.length > 1 && (
                <FormControl size="small" sx={{ minWidth: 130 }}>
                  <InputLabel>Transform</InputLabel>
                  <Select
                    value={filter.transformId ?? ''}
                    label="Transform"
                    onChange={(e) => handleTransformSelect(e.target.value as number)}
                  >
                    {stringTransforms.map((t) => (
                      <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>
                    ))}
                  </Select>
                </FormControl>
              )}
            </>
          ) : (
            // NumericBin transforms exist but can't be used in filters
            transforms.some(
              (t) => t.fieldName === filter.fieldName && t.type === 'NumericBin',
            ) && (
              <Tooltip title="Numeric bin transforms cannot be used as filter conditions (OR-range logic not supported). Use raw value filters for numeric fields.">
                <Typography variant="caption" color="text.disabled" sx={{ cursor: 'default' }}>
                  (numeric bins not filterable)
                </Typography>
              </Tooltip>
            )
          )}
        </Box>
      )}

      {/* ── Values multi-select ── */}
      <FormControl size="small" sx={{ flex: 1, minWidth: 160 }}>
        <InputLabel>{valuesLabel}</InputLabel>
        <Select
          multiple
          value={filter.values}
          disabled={!filter.fieldName || (!filter.useTransform && isFetching)}
          onChange={(e) => handleValuesChange(e.target.value)}
          input={<OutlinedInput label={valuesLabel} />}
          renderValue={(sel) => (
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {sel.map((v) => <Chip key={v} label={v} size="small" />)}
            </Box>
          )}
        >
          {!filter.useTransform && isFetching ? (
            <MenuItem disabled><em>Loading values…</em></MenuItem>
          ) : displayValues.length === 0 ? (
            <MenuItem disabled><em>No values found</em></MenuItem>
          ) : (
            displayValues.map((v) => (
              <MenuItem key={v} value={v}>{v}</MenuItem>
            ))
          )}
        </Select>
      </FormControl>

      <IconButton size="small" onClick={onRemove} title="Remove filter" sx={{ mt: 0.25 }}>
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
