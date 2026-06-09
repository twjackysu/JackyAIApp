import CloseIcon from '@mui/icons-material/Close';
import EditIcon from '@mui/icons-material/Edit';
import SaveIcon from '@mui/icons-material/Save';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';

import { DATE_GRANULARITIES, TRANSFORM_TYPE_LABELS } from '../../constants';

import { typeChipColor, type DraftState } from './helpers';

import type { DateGranularity, FieldDefinition } from '../../../apis/dataAnalysisApis/types';

interface AddTransformFormProps {
  fields: FieldDefinition[];
  draft: DraftState;
  setDraft: React.Dispatch<React.SetStateAction<DraftState>>;
  onFieldChange: (fieldName: string) => void;
  onSaveDateTrunc: () => void;
  onConfigure: () => void;
  onCancel: () => void;
}

export function AddTransformForm({
  fields,
  draft,
  setDraft,
  onFieldChange,
  onSaveDateTrunc,
  onConfigure,
  onCancel,
}: AddTransformFormProps) {
  return (
    <Paper
      variant="outlined"
      sx={{ p: 1.5, mt: 1, display: 'flex', gap: 1, alignItems: 'flex-end', flexWrap: 'wrap' }}
    >
      {/* Field selector — drives type auto-detection */}
      <FormControl size="small" sx={{ flex: '0 0 160px' }}>
        <InputLabel>Field</InputLabel>
        <Select
          value={draft.fieldName}
          label="Field"
          onChange={(e) => onFieldChange(e.target.value as string)}
        >
          {fields.map((f) => (
            <MenuItem key={f.name} value={f.name}>
              {f.name}
            </MenuItem>
          ))}
        </Select>
      </FormControl>

      {/* Name */}
      <TextField
        size="small"
        label="Name"
        placeholder={
          !draft.fieldName
            ? 'Select a field first'
            : draft.type === 'DateTrunc'
              ? 'e.g. Order Month'
              : draft.type === 'NumericBin'
                ? 'e.g. Age Groups'
                : 'e.g. Country → Region'
        }
        value={draft.name}
        sx={{ flex: '1 1 150px' }}
        onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
      />

      {/* Auto-detected type chip (read-only) */}
      {draft.fieldName && (
        <Tooltip title="Transform type is auto-detected from the field's data type">
          <Chip
            label={TRANSFORM_TYPE_LABELS[draft.type]}
            size="small"
            color={typeChipColor(draft.type)}
            sx={{ mb: 0.5, flexShrink: 0 }}
          />
        </Tooltip>
      )}

      {/* DateTrunc: inline granularity + Save */}
      {draft.type === 'DateTrunc' && draft.fieldName && (
        <>
          <FormControl size="small" sx={{ flex: '0 0 110px' }}>
            <InputLabel>Granularity</InputLabel>
            <Select
              value={draft.granularity}
              label="Granularity"
              onChange={(e) =>
                setDraft((d) => ({ ...d, granularity: e.target.value as DateGranularity }))
              }
            >
              {DATE_GRANULARITIES.map((g) => (
                <MenuItem key={g} value={g}>
                  {g}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <Button
            size="small"
            variant="contained"
            startIcon={<SaveIcon fontSize="small" />}
            onClick={onSaveDateTrunc}
          >
            Save
          </Button>
        </>
      )}

      {/* NumericBin / StringGrouping: Configure button */}
      {draft.type !== 'DateTrunc' && draft.fieldName && (
        <Button
          size="small"
          variant="contained"
          startIcon={<EditIcon fontSize="small" />}
          onClick={onConfigure}
        >
          Configure
        </Button>
      )}

      <IconButton size="small" onClick={onCancel} title="Cancel">
        <CloseIcon fontSize="small" />
      </IconButton>
    </Paper>
  );
}
