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

import { DATE_GRANULARITIES, TRANSFORM_TYPE_LABELS } from '../../constants';
import { FieldSelect } from '../FieldSelect';

import { typeChipColor, type DraftState } from './helpers';

import type { DateGranularity, FieldDefinition } from '../../../apis/dataAnalysisApis/types';

import { ActionTooltip, ClickPopover } from '@/components/hints';

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
      <FieldSelect fields={fields} value={draft.fieldName} onChange={onFieldChange} />

      {/* Name */}
      <TextField
        size="small"
        label="名稱"
        placeholder={
          !draft.fieldName
            ? '請先選擇欄位'
            : draft.type === 'DateTrunc'
              ? '例如：訂單月份'
              : draft.type === 'NumericBin'
                ? '例如：年齡分組'
                : '例如：國家 → 地區'
        }
        value={draft.name}
        sx={{ flex: '1 1 150px' }}
        onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
      />

      {/* Auto-detected type chip (read-only) */}
      {draft.fieldName && (
        <ClickPopover content="轉換類型會依欄位的資料型別自動偵測">
          <Chip
            label={TRANSFORM_TYPE_LABELS[draft.type]}
            size="small"
            color={typeChipColor(draft.type)}
            sx={{ mb: 0.5, flexShrink: 0 }}
          />
        </ClickPopover>
      )}

      {/* DateTrunc: inline granularity + Save */}
      {draft.type === 'DateTrunc' && draft.fieldName && (
        <>
          <FormControl size="small" sx={{ flex: '0 0 110px' }}>
            <InputLabel>時間粒度</InputLabel>
            <Select
              value={draft.granularity}
              label="時間粒度"
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
            儲存
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
          設定
        </Button>
      )}

      <ActionTooltip title="取消">
        <IconButton size="small" onClick={onCancel}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </ActionTooltip>
    </Paper>
  );
}
