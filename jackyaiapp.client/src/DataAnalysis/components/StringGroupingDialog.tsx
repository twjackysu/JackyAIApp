import DeleteIcon from '@mui/icons-material/Delete';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import FormHelperText from '@mui/material/FormHelperText';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import OutlinedInput from '@mui/material/OutlinedInput';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import { useGetDistinctValuesQuery } from '../../apis/dataAnalysisApis';

import { FieldGroupingDialogShell } from './FieldGroupingDialogShell';

import type { GroupEntry } from '../types';

// ── Validation ────────────────────────────────────────────────────────────────

interface GroupError {
  emptyLabel: boolean;
  noValues: boolean;
}

interface GroupValidation {
  perGroup: GroupError[];
  /** Values that appear in more than one group: value → group indices */
  duplicates: Map<string, number[]>;
  hasErrors: boolean;
}

function validateGroups(groups: GroupEntry[]): GroupValidation {
  const perGroup: GroupError[] = groups.map((g) => ({
    emptyLabel: !g.label.trim(),
    noValues: g.values.length === 0,
  }));

  // Detect values that are assigned to multiple groups
  const valueToGroups = new Map<string, number[]>();
  groups.forEach((g, i) => {
    g.values.forEach((v) => {
      if (!valueToGroups.has(v)) valueToGroups.set(v, []);
      valueToGroups.get(v)!.push(i);
    });
  });

  const duplicates = new Map<string, number[]>();
  valueToGroups.forEach((indices, v) => {
    if (indices.length > 1) duplicates.set(v, indices);
  });

  const hasErrors = perGroup.some((e) => e.emptyLabel || e.noValues) || duplicates.size > 0;

  return { perGroup, duplicates, hasErrors };
}

// ── Component ─────────────────────────────────────────────────────────────────

interface StringGroupingDialogProps {
  open: boolean;
  onClose: () => void;
  onSave: (groups: GroupEntry[], fallback: string) => void;
  fieldName: string;
  initialGroups: GroupEntry[];
  initialFallback: string;
  sourceId?: string;
  tableName?: string;
}

export function StringGroupingDialog({
  open,
  onClose,
  onSave,
  fieldName,
  initialGroups,
  initialFallback,
  sourceId,
  tableName,
}: StringGroupingDialogProps) {
  const [groups, setGroups] = useState<GroupEntry[]>([]);
  const [fallback, setFallback] = useState('Other');

  useEffect(() => {
    if (open) {
      setGroups(initialGroups.map((g) => ({ ...g, values: [...g.values] })));
      setFallback(initialFallback || 'Other');
    }
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const { data: lvData, isFetching } = useGetDistinctValuesQuery(
    { field: fieldName, sourceId, tableName },
    { skip: !fieldName || !open },
  );
  const allValues = lvData?.data?.values ?? [];

  const addGroup = () => setGroups((prev) => [...prev, { id: Date.now(), label: '', values: [] }]);
  const removeGroup = (id: number) => setGroups((prev) => prev.filter((g) => g.id !== id));
  const updateGroup = (id: number, patch: Partial<GroupEntry>) =>
    setGroups((prev) => prev.map((g) => (g.id === id ? { ...g, ...patch } : g)));

  // Live validation — recomputed every render
  const { perGroup, duplicates, hasErrors } = validateGroups(groups);

  // Values assigned anywhere — used to visually distinguish already-mapped values
  const assignedValues = new Set(groups.flatMap((g) => g.values));

  const handleApply = () => {
    if (!hasErrors) onSave(groups, fallback);
  };

  return (
    <FieldGroupingDialogShell
      open={open}
      onClose={onClose}
      title="文字分組 (String Groups)"
      fieldName={fieldName}
      hint="將原始數值對應到邏輯群組。每個數值最多只能屬於一個群組。未對應的資料會使用預設標籤。"
      addLabel="新增群組"
      onAdd={addGroup}
      fallbackLabel="預設標籤（未對應任何群組的數值）"
      fallback={fallback}
      onFallbackChange={setFallback}
      hasErrors={hasErrors}
      onApply={handleApply}
    >
      {/* Duplicate value warning */}
      {duplicates.size > 0 && (
        <Alert severity="error" sx={{ mb: 2 }}>
          <strong>以下數值被指派到多個群組：</strong>
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mt: 0.5 }}>
            {Array.from(duplicates.entries()).map(([v, indices]) => (
              <Chip
                key={v}
                label={`"${v}" — 群組 ${indices.map((i) => i + 1).join(' & ')}`}
                size="small"
                color="error"
                variant="outlined"
              />
            ))}
          </Box>
        </Alert>
      )}

      {groups.length === 0 && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          尚無群組 — 點擊 <strong>新增群組</strong> 開始。
        </Typography>
      )}

      {groups.map((g, i) => {
        const e = perGroup[i];
        return (
          <Box key={g.id} sx={{ mb: 2 }}>
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start' }}>
              {/* Group label */}
              <TextField
                size="small"
                label={`群組 ${i + 1} 標籤 *`}
                value={g.label}
                error={e.emptyLabel}
                helperText={e.emptyLabel ? '標籤為必填。' : ' '}
                sx={{ flex: '0 0 160px' }}
                onChange={(ev) => updateGroup(g.id, { label: ev.target.value })}
              />

              {/* Value picker */}
              <FormControl size="small" sx={{ flex: 1 }} error={e.noValues}>
                <InputLabel>{isFetching ? '載入中…' : '數值 *'}</InputLabel>
                <Select
                  multiple
                  value={g.values}
                  disabled={isFetching}
                  onChange={(ev) =>
                    updateGroup(g.id, {
                      values:
                        typeof ev.target.value === 'string'
                          ? ev.target.value.split(',')
                          : (ev.target.value as string[]),
                    })
                  }
                  input={<OutlinedInput label={isFetching ? '載入中…' : '數值 *'} />}
                  renderValue={(sel) => (
                    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                      {sel.map((v) => (
                        <Chip
                          key={v}
                          label={v}
                          size="small"
                          color={duplicates.has(v) ? 'error' : 'default'}
                        />
                      ))}
                    </Box>
                  )}
                >
                  {isFetching ? (
                    <MenuItem disabled>
                      <em>載入中…</em>
                    </MenuItem>
                  ) : allValues.length === 0 ? (
                    <MenuItem disabled>
                      <em>找不到數值</em>
                    </MenuItem>
                  ) : (
                    allValues.map((v) => (
                      <MenuItem
                        key={v}
                        value={v}
                        sx={{
                          // Visually mark values already assigned to OTHER groups
                          opacity: assignedValues.has(v) && !g.values.includes(v) ? 0.45 : 1,
                        }}
                      >
                        {v}
                        {assignedValues.has(v) && !g.values.includes(v) && (
                          <Typography variant="caption" color="warning.main" sx={{ ml: 1 }}>
                            （已對應）
                          </Typography>
                        )}
                      </MenuItem>
                    ))
                  )}
                </Select>
                <FormHelperText>{e.noValues ? '請至少選擇一個數值。' : ' '}</FormHelperText>
              </FormControl>

              <IconButton size="small" onClick={() => removeGroup(g.id)} sx={{ mt: 0.5 }}>
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Box>
          </Box>
        );
      })}
    </FieldGroupingDialogShell>
  );
}
