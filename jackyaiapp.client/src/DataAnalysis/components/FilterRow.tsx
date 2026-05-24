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
import { useEffect, useState } from 'react';

import { useSummarizeMutation, useGetDistinctValuesQuery } from '../../apis/dataAnalysisApis';
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
  // All transforms for this field (any type)
  const fieldTransforms = transforms.filter((t) => t.fieldName === filter.fieldName);
  const hasTransforms   = fieldTransforms.length > 0;

  // Resolve the active transform (if any)
  const activeTransform = filter.useTransform && filter.transformId !== null
    ? transforms.find((t) => t.id === filter.transformId)
    : undefined;

  // ── DateTrunc: fetch distinct truncated period values via Summarize ────────
  const [fetchSummarize] = useSummarizeMutation();
  const [dateTruncValues,   setDateTruncValues]   = useState<string[]>([]);
  const [dateTruncFetching, setDateTruncFetching] = useState(false);

  useEffect(() => {
    if (!filter.useTransform || !activeTransform || activeTransform.type !== 'DateTrunc') {
      setDateTruncValues([]);
      return;
    }

    setDateTruncFetching(true);
    setDateTruncValues([]);

    const ctx = sourceId || tableName ? { sourceId, tableName } : undefined;
    const done = () => setDateTruncFetching(false);
    fetchSummarize({
      context: ctx,
      viewBy: [{
        type:        'DateTrunc',
        fieldName:   activeTransform.fieldName,
        granularity: activeTransform.granularity,
      }],
      measures: [{ fieldName: activeTransform.fieldName, aggregation: 'Count', alias: '_cnt' }],
    }).then(
      (result) => {
        if ('data' in result && result.data?.data?.rows) {
          const rows   = result.data.data.rows;
          const dimKey = rows.length > 0 ? Object.keys(rows[0])[0] : activeTransform.fieldName;
          const vals   = rows
            .map((r) => String(r[dimKey] ?? ''))
            .filter(Boolean)
            .sort();
          setDateTruncValues(vals);
        }
        done();
      },
      done,
    );
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filter.useTransform, activeTransform?.id, filter.fieldName, sourceId, tableName]);

  // ── Raw distinct values (skipped while in transform mode) ─────────────────
  const { data: lvData, isFetching } = useGetDistinctValuesQuery(
    { field: filter.fieldName, sourceId, tableName },
    { skip: !filter.fieldName || filter.useTransform },
  );
  const rawDistinctValues = lvData?.data?.values ?? [];

  // ── Values shown in the multi-select ──────────────────────────────────────
  const displayValues: string[] = (() => {
    if (!filter.useTransform || !activeTransform) return rawDistinctValues;
    switch (activeTransform.type) {
      case 'StringGrouping': return activeTransform.groups.map((g) => g.label);
      case 'NumericBin':     return activeTransform.bins.map((b) => b.label);
      case 'DateTrunc':      return dateTruncValues;
      default:               return rawDistinctValues;
    }
  })();

  const isLoadingValues = filter.useTransform
    ? (activeTransform?.type === 'DateTrunc' && dateTruncFetching)
    : (!filter.useTransform && isFetching);

  const valuesLabel = isLoadingValues
    ? 'Loading…'
    : filter.fieldName
      ? filter.useTransform
        ? activeTransform?.type === 'NumericBin' ? 'Bin labels'
          : activeTransform?.type === 'DateTrunc' ? 'Period labels'
          : 'Group labels'
        : 'Values'
      : 'Select a field first';

  // ── Tooltip copy for the "Use transform" switch ────────────────────────────
  const transformTooltip = activeTransform?.type === 'StringGrouping'
    ? 'Filter by group labels (each label expands to its raw member values)'
    : activeTransform?.type === 'NumericBin'
      ? 'Filter by bin labels (selected bins are merged into a range filter)'
      : 'Filter by period labels (selected periods are merged into a date range)';

  // ── Handlers ──────────────────────────────────────────────────────────────

  const handleFieldChange = (fieldName: string) => {
    onUpdate({ ...filter, fieldName, values: [], useTransform: false, transformId: null });
  };

  const handleTransformToggle = (checked: boolean) => {
    if (checked) {
      const first = fieldTransforms[0];
      onUpdate({ ...filter, useTransform: true, transformId: first?.id ?? null, values: [] });
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

      {/* ── Transform controls (when any transform exists for this field) ── */}
      {filter.fieldName && hasTransforms && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Tooltip title={filter.useTransform ? transformTooltip : 'Switch on to filter using transform labels'} placement="top">
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
          {filter.useTransform && fieldTransforms.length > 1 && (
            <FormControl size="small" sx={{ minWidth: 140 }}>
              <InputLabel>Transform</InputLabel>
              <Select
                value={filter.transformId ?? ''}
                label="Transform"
                onChange={(e) => handleTransformSelect(e.target.value as number)}
              >
                {fieldTransforms.map((t) => (
                  <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>
                ))}
              </Select>
            </FormControl>
          )}
        </Box>
      )}

      {/* ── Values multi-select ── */}
      <FormControl size="small" sx={{ flex: 1, minWidth: 160 }}>
        <InputLabel>{valuesLabel}</InputLabel>
        <Select
          multiple
          value={filter.values}
          disabled={!filter.fieldName || isLoadingValues}
          onChange={(e) => handleValuesChange(e.target.value)}
          input={<OutlinedInput label={valuesLabel} />}
          renderValue={(sel) => (
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {sel.map((v) => <Chip key={v} label={v} size="small" />)}
            </Box>
          )}
        >
          {isLoadingValues ? (
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
