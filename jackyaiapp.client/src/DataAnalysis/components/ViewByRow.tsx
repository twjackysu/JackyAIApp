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
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { FieldTransformDef, ViewByEntry } from '../types';

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
        <InputLabel>Field</InputLabel>
        <Select
          value={entry.fieldName}
          label="Field"
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
            <Tooltip
              title={
                entry.type === 'Transform'
                  ? 'Group values will be bucketed / renamed by the selected transform'
                  : 'Switch on to apply a field transform when grouping'
              }
              placement="top"
            >
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
                    Use transform
                  </Typography>
                }
              />
            </Tooltip>

            {/* Transform picker — shown when toggle is ON and multiple choices exist */}
            {entry.type === 'Transform' && fieldTransforms.length > 1 && (
              <FormControl size="small" sx={{ minWidth: 150 }}>
                <InputLabel>Transform</InputLabel>
                <Select
                  value={entry.transformId ?? ''}
                  label="Transform"
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
        label="Alias"
        placeholder="output col name"
        value={entry.alias}
        sx={{ flex: '0 0 130px' }}
        onChange={(e) => onUpdate({ ...entry, alias: e.target.value })}
      />

      <IconButton size="small" onClick={onRemove} title="Remove" sx={{ mt: 0.25 }}>
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
