import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { FieldTransformDef, ViewByEntry } from '../types';

import { InfoPopover } from '@/components/hints';

interface ViewByRowProps {
  entry: ViewByEntry;
  fields: FieldDefinition[];
  transforms: FieldTransformDef[];
  onUpdate: (e: ViewByEntry) => void;
  onRemove: () => void;
}

export function ViewByRow({ entry, fields, transforms, onUpdate, onRemove }: ViewByRowProps) {
  const fieldTransforms = transforms.filter((t) => t.fieldName === entry.fieldName);
  const hasTransforms = fieldTransforms.length > 0;

  const handleFieldChange = (fieldName: string) => {
    const newFieldTransforms = transforms.filter((t) => t.fieldName === fieldName);
    // Keep transform mode only if the new field also has transforms
    const keepTransform = entry.type === 'Transform' && newFieldTransforms.length > 0;
    onUpdate({
      ...entry,
      fieldName,
      type: keepTransform ? 'Transform' : 'Plain',
      transformId: keepTransform ? (newFieldTransforms[0]?.id ?? null) : null,
    });
  };

  const handleToggle = (checked: boolean) => {
    if (checked) {
      onUpdate({ ...entry, type: 'Transform', transformId: fieldTransforms[0]?.id ?? null });
    } else {
      onUpdate({ ...entry, type: 'Plain', transformId: null });
    }
  };

  const handleTransformSelect = (transformId: number) => {
    onUpdate({ ...entry, transformId });
  };

  return (
    <Box sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {/* Field */}
      <FormControl size="small" sx={{ flex: '0 0 160px' }}>
        <InputLabel>欄位</InputLabel>
        <Select
          value={entry.fieldName}
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

      {/* Use transform toggle — only shown when transforms exist for this field */}
      {entry.fieldName &&
        (hasTransforms ? (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
            {/* The switch owns its own click, so the explanation sits in a button beside it
                rather than wrapping it. */}
            <FormControlLabel
              sx={{ m: 0 }}
              control={
                <Switch
                  size="small"
                  checked={entry.type === 'Transform'}
                  onChange={(e) => handleToggle(e.target.checked)}
                />
              }
              label={
                <Typography variant="caption" color="text.secondary">
                  套用轉換
                </Typography>
              }
            />
            <InfoPopover
              content={
                entry.type === 'Transform'
                  ? '分組值會依所選轉換被歸類 / 重新命名'
                  : '開啟以在分組時套用欄位轉換'
              }
              ariaLabel="套用轉換說明"
              iconSize={14}
            />

            {/* Transform picker — shown when toggle is ON and multiple choices exist */}
            {entry.type === 'Transform' && fieldTransforms.length > 1 && (
              <FormControl size="small" sx={{ minWidth: 150 }}>
                <InputLabel>轉換</InputLabel>
                <Select
                  value={entry.transformId ?? ''}
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
        ) : /* Field selected but no transforms defined — silent, no clutter */
        null)}

      {/* Alias */}
      <TextField
        size="small"
        label="別名"
        placeholder="輸出欄位名稱"
        value={entry.alias}
        sx={{ flex: '0 0 130px' }}
        onChange={(e) => onUpdate({ ...entry, alias: e.target.value })}
      />

      <IconButton size="small" onClick={onRemove} title="移除" sx={{ mt: 0.25 }}>
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
