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
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import { useSummarizeMutation, useGetDistinctValuesQuery } from '../../apis/dataAnalysisApis';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef } from '../types';

import { ActionTooltip, InfoPopover } from '@/components/hints';

interface FilterRowProps {
  filter: ActiveFilter;
  fields: FieldDefinition[];
  transforms: FieldTransformDef[];
  onUpdate: (f: ActiveFilter) => void;
  onRemove: () => void;
  sourceId?: string;
  tableName?: string;
}

export function FilterRow({
  filter,
  fields,
  transforms,
  onUpdate,
  onRemove,
  sourceId,
  tableName,
}: FilterRowProps) {
  // All transforms for this field (any type)
  const fieldTransforms = transforms.filter((t) => t.fieldName === filter.fieldName);
  const hasTransforms = fieldTransforms.length > 0;

  // Resolve the active transform (if any)
  const activeTransform =
    filter.useTransform && filter.transformId !== null
      ? transforms.find((t) => t.id === filter.transformId)
      : undefined;

  // ── DateTrunc: fetch distinct truncated period values via Summarize ────────
  const [fetchSummarize] = useSummarizeMutation();
  const [dateTruncValues, setDateTruncValues] = useState<string[]>([]);
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
      viewBy: [
        {
          type: 'DateTrunc',
          fieldName: activeTransform.fieldName,
          granularity: activeTransform.granularity,
        },
      ],
      measures: [{ fieldName: activeTransform.fieldName, aggregation: 'Count', alias: '_cnt' }],
    }).then((result) => {
      if ('data' in result && result.data?.data?.rows) {
        const rows = result.data.data.rows;
        const dimKey = rows.length > 0 ? Object.keys(rows[0])[0] : activeTransform.fieldName;
        const vals = rows
          .map((r) => String(r[dimKey] ?? ''))
          .filter(Boolean)
          .sort();
        setDateTruncValues(vals);
      }
      done();
    }, done);
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
      case 'StringGrouping':
        return activeTransform.groups.map((g) => g.label);
      case 'NumericBin':
        return activeTransform.bins.map((b) => b.label);
      case 'DateTrunc':
        return dateTruncValues;
      default:
        return rawDistinctValues;
    }
  })();

  const isLoadingValues = filter.useTransform
    ? activeTransform?.type === 'DateTrunc' && dateTruncFetching
    : !filter.useTransform && isFetching;

  const valuesLabel = isLoadingValues
    ? '載入中…'
    : filter.fieldName
      ? filter.useTransform
        ? activeTransform?.type === 'NumericBin'
          ? '區間標籤'
          : activeTransform?.type === 'DateTrunc'
            ? '期間標籤'
            : '群組標籤'
        : '數值'
      : '請先選擇欄位';

  // ── Explanatory copy for the "Use transform" switch ───────────────────────
  const transformHint =
    activeTransform?.type === 'StringGrouping'
      ? '依群組標籤篩選（每個標籤會展開為對應的原始數值）'
      : activeTransform?.type === 'NumericBin'
        ? '依區間標籤篩選（選取的區間會合併成範圍篩選）'
        : '依期間標籤篩選（選取的期間會合併成日期範圍）';

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
        <InputLabel>欄位</InputLabel>
        <Select
          value={filter.fieldName}
          label="欄位"
          onChange={(e) => handleFieldChange(e.target.value as string)}
        >
          {fields.map((f) => (
            <MenuItem key={f.name} value={f.name}>
              {f.name}
            </MenuItem>
          ))}
        </Select>
      </FormControl>

      {/* ── Transform controls (when any transform exists for this field) ── */}
      {filter.fieldName && hasTransforms && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          {/* The switch owns its own click, so the explanation sits in a button beside it
              rather than wrapping it. */}
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
                套用轉換
              </Typography>
            }
          />
          <InfoPopover
            content={filter.useTransform ? transformHint : '開啟以使用轉換標籤篩選'}
            ariaLabel="套用轉換說明"
            iconSize={14}
          />

          {/* When multiple transforms exist for the field, let user pick */}
          {filter.useTransform && fieldTransforms.length > 1 && (
            <FormControl size="small" sx={{ minWidth: 140 }}>
              <InputLabel>轉換</InputLabel>
              <Select
                value={filter.transformId ?? ''}
                label="轉換"
                onChange={(e) => handleTransformSelect(e.target.value as number)}
              >
                {fieldTransforms.map((t) => (
                  <MenuItem key={t.id} value={t.id}>
                    {t.name}
                  </MenuItem>
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
              {sel.map((v) => (
                <Chip key={v} label={v} size="small" />
              ))}
            </Box>
          )}
        >
          {isLoadingValues ? (
            <MenuItem disabled>
              <em>載入數值中…</em>
            </MenuItem>
          ) : displayValues.length === 0 ? (
            <MenuItem disabled>
              <em>找不到數值</em>
            </MenuItem>
          ) : (
            displayValues.map((v) => (
              <MenuItem key={v} value={v}>
                {v}
              </MenuItem>
            ))
          )}
        </Select>
      </FormControl>

      <ActionTooltip title="移除篩選條件">
        <IconButton size="small" onClick={onRemove} sx={{ mt: 0.25 }}>
          <DeleteIcon fontSize="small" />
        </IconButton>
      </ActionTooltip>
    </Box>
  );
}
