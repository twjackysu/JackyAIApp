import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import { VIEW_BY_TYPE_LABELS } from '../constants';
import type { FieldTransformDef, ViewByEntry } from '../types';

interface ViewByRowProps {
  entry:      ViewByEntry;
  fields:     FieldDefinition[];
  transforms: FieldTransformDef[];
  onUpdate:   (e: ViewByEntry) => void;
  onRemove:   () => void;
}

export function ViewByRow({ entry, fields, transforms, onUpdate, onRemove }: ViewByRowProps) {
  const fieldTransforms = transforms.filter((t) => t.fieldName === entry.fieldName);
  const hasTransforms   = fieldTransforms.length > 0;

  const handleFieldChange = (fieldName: string) => {
    const newFieldTransforms = transforms.filter((t) => t.fieldName === fieldName);
    const newType: ViewByEntry['type'] =
      entry.type === 'Transform' && newFieldTransforms.length === 0 ? 'Plain' : entry.type;
    const newTransformId = newType === 'Transform' ? (newFieldTransforms[0]?.id ?? null) : null;
    onUpdate({ ...entry, fieldName, type: newType, transformId: newTransformId });
  };

  const handleTypeChange = (type: ViewByEntry['type']) => {
    const transformId = type === 'Transform' ? (fieldTransforms[0]?.id ?? null) : null;
    onUpdate({ ...entry, type, transformId });
  };

  return (
    <Box sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {/* Field */}
      <FormControl size="small" sx={{ flex: '0 0 160px' }}>
        <InputLabel>Field</InputLabel>
        <Select
          value={entry.fieldName} label="Field"
          onChange={(e) => handleFieldChange(e.target.value as string)}
        >
          {fields.map((f) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}
        </Select>
      </FormControl>

      {/* Type */}
      <FormControl size="small" sx={{ flex: '0 0 150px' }}>
        <InputLabel>Type</InputLabel>
        <Select
          value={entry.type} label="Type"
          onChange={(e) => handleTypeChange(e.target.value as ViewByEntry['type'])}
        >
          {(Object.keys(VIEW_BY_TYPE_LABELS) as ViewByEntry['type'][]).map((t) => (
            <MenuItem key={t} value={t} disabled={t === 'Transform' && !hasTransforms}>
              <Tooltip
                title={t === 'Transform' && !hasTransforms ? 'No transforms defined for this field' : ''}
                placement="right"
              >
                <span style={{ width: '100%' }}>{VIEW_BY_TYPE_LABELS[t]}</span>
              </Tooltip>
            </MenuItem>
          ))}
        </Select>
      </FormControl>

      {/* Transform selector (when type === Transform) */}
      {entry.type === 'Transform' && (
        fieldTransforms.length === 0 ? (
          <Typography variant="caption" color="warning.main" sx={{ mt: 1 }}>
            No transforms for this field — define one in Field Transforms above.
          </Typography>
        ) : (
          <FormControl size="small" sx={{ flex: '1 1 160px' }}>
            <InputLabel>Transform</InputLabel>
            <Select
              value={entry.transformId ?? ''}
              label="Transform"
              onChange={(e) => onUpdate({ ...entry, transformId: e.target.value as number })}
            >
              {fieldTransforms.map((t) => (
                <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>
              ))}
            </Select>
          </FormControl>
        )
      )}

      {/* Alias */}
      <TextField
        size="small" label="Alias" placeholder="output col name"
        value={entry.alias} sx={{ flex: '0 0 130px' }}
        onChange={(e) => onUpdate({ ...entry, alias: e.target.value })}
      />

      <IconButton size="small" onClick={onRemove} title="Remove" sx={{ mt: 0.25 }}>
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
