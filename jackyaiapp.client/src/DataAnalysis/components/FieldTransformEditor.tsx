import AddIcon from '@mui/icons-material/Add';
import CloseIcon from '@mui/icons-material/Close';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import SaveIcon from '@mui/icons-material/Save';
import TransformIcon from '@mui/icons-material/Transform';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import {
  DATE_CANONICAL_TYPES,
  DATE_GRANULARITIES,
  NUMERIC_CANONICAL_TYPES,
  TRANSFORM_TYPE_LABELS,
} from '../constants';

import { NumericBinDialog } from './NumericBinDialog';
import { StringGroupingDialog } from './StringGroupingDialog';

import type {
  ColumnDataType,
  DateGranularity,
  FieldDefinition,
} from '../../apis/dataAnalysisApis/types';
import type { BinEntry, FieldTransformDef, GroupEntry } from '../types';

interface FieldTransformEditorProps {
  transforms: FieldTransformDef[];
  onAdd: (t: Omit<FieldTransformDef, 'id'>) => void;
  onUpdate: (t: FieldTransformDef) => void;
  onRemove: (id: number) => void;
  fields: FieldDefinition[];
  sourceId?: string;
  tableName?: string;
}

// ── Type inference from CanonicalType ─────────────────────────────────────────

function inferTransformType(canonicalType: ColumnDataType): FieldTransformDef['type'] {
  if ((DATE_CANONICAL_TYPES as ColumnDataType[]).includes(canonicalType)) return 'DateTrunc';
  if ((NUMERIC_CANONICAL_TYPES as ColumnDataType[]).includes(canonicalType)) return 'NumericBin';
  return 'StringGrouping';
}

// ── Draft (add-form) state ────────────────────────────────────────────────────

interface DraftState {
  fieldName: string;
  name: string;
  /** Auto-derived from the field's canonicalType; not user-editable. */
  type: FieldTransformDef['type'];
  granularity: DateGranularity;
}

const emptyDraft = (): DraftState => ({
  fieldName: '',
  name: '',
  type: 'StringGrouping',
  granularity: 'Month',
});

// ── Display helpers ───────────────────────────────────────────────────────────

const typeChipColor = (type: FieldTransformDef['type']) => {
  if (type === 'DateTrunc') return 'info' as const;
  if (type === 'NumericBin') return 'warning' as const;
  return 'secondary' as const;
};

const summary = (t: FieldTransformDef): string => {
  if (t.type === 'DateTrunc') return t.granularity;
  if (t.type === 'NumericBin') return `${t.bins.length} bin${t.bins.length !== 1 ? 's' : ''}`;
  return `${t.groups.length} group${t.groups.length !== 1 ? 's' : ''}`;
};

// ── Component ─────────────────────────────────────────────────────────────────

export function FieldTransformEditor({
  transforms,
  onAdd,
  onUpdate,
  onRemove,
  fields,
  sourceId,
  tableName,
}: FieldTransformEditorProps) {
  const [showAddForm, setShowAddForm] = useState(false);
  const [draft, setDraft] = useState<DraftState>(emptyDraft);

  // Target for NumericBin / StringGrouping configure dialogs
  const [configTarget, setConfigTarget] = useState<FieldTransformDef | null>(null);
  // Target for DateTrunc edit dialog
  const [dateTruncEditTarget, setDateTruncEditTarget] = useState<FieldTransformDef | null>(null);

  // ── Field selection: auto-derive transform type ───────────────────────────

  const handleDraftFieldChange = (fieldName: string) => {
    const canonicalType = fields.find((f) => f.name === fieldName)?.canonicalType ?? 'String';
    const type = inferTransformType(canonicalType);
    setDraft((d) => ({ ...d, fieldName, type }));
  };

  // ── Effective name when user left it blank ────────────────────────────────

  const effectiveName = (d: DraftState) =>
    d.name.trim() ||
    (d.type === 'DateTrunc'
      ? `${d.fieldName} (${d.granularity})`
      : `${d.fieldName} ${d.type === 'NumericBin' ? 'Bins' : 'Groups'}`);

  // ── Add-form: DateTrunc save (no dialog needed) ───────────────────────────

  const handleSaveDateTruncNew = () => {
    onAdd({
      fieldName: draft.fieldName,
      name: effectiveName(draft),
      type: 'DateTrunc',
      granularity: draft.granularity,
      bins: [],
      groups: [],
      fallback: '',
    });
    setShowAddForm(false);
    setDraft(emptyDraft());
  };

  // ── Add-form: NumericBin / StringGrouping → open configure dialog ─────────

  const handleOpenConfigure = () => {
    setConfigTarget({
      id: -1,
      fieldName: draft.fieldName,
      name: effectiveName(draft),
      type: draft.type as 'NumericBin' | 'StringGrouping',
      granularity: 'Month',
      bins: [],
      groups: [],
      fallback: 'Other',
    });
  };

  const handleCancelAdd = () => {
    setShowAddForm(false);
    setDraft(emptyDraft());
    setConfigTarget(null);
  };

  // ── Save handlers from configure dialogs ─────────────────────────────────

  const handleSaveNumericBin = (bins: BinEntry[], fallback: string) => {
    if (!configTarget) return;
    if (configTarget.id === -1) {
      onAdd({ ...configTarget, bins, fallback });
      setShowAddForm(false);
      setDraft(emptyDraft());
    } else {
      onUpdate({ ...configTarget, bins, fallback });
    }
    setConfigTarget(null);
  };

  const handleSaveStringGrouping = (groups: GroupEntry[], fallback: string) => {
    if (!configTarget) return;
    if (configTarget.id === -1) {
      onAdd({ ...configTarget, groups, fallback });
      setShowAddForm(false);
      setDraft(emptyDraft());
    } else {
      onUpdate({ ...configTarget, groups, fallback });
    }
    setConfigTarget(null);
  };

  // ── Edit existing ─────────────────────────────────────────────────────────

  const handleEdit = (t: FieldTransformDef) => {
    if (t.type === 'DateTrunc') setDateTruncEditTarget({ ...t });
    else setConfigTarget({ ...t });
  };

  // ── DateTrunc edit dialog save ────────────────────────────────────────────

  const handleSaveDateTruncEdit = () => {
    if (dateTruncEditTarget) onUpdate(dateTruncEditTarget);
    setDateTruncEditTarget(null);
  };

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <Box>
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <TransformIcon fontSize="small" color="action" />
          <Typography variant="subtitle2">
            Field Transforms
            {transforms.length > 0 && (
              <Chip label={transforms.length} size="small" sx={{ ml: 1 }} />
            )}
          </Typography>
        </Box>
        {!showAddForm && (
          <Tooltip title="Add transform">
            <IconButton size="small" onClick={() => setShowAddForm(true)}>
              <AddIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        )}
      </Box>

      {/* Empty state */}
      {transforms.length === 0 && !showAddForm && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
          No transforms — click <strong>+</strong> to create one. Transforms define how a field's
          values are bucketed or renamed, and can be applied to both Group By and Filters.
        </Typography>
      )}

      {/* Existing transform list */}
      {transforms.map((t) => (
        <Paper
          key={t.id}
          variant="outlined"
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1,
            px: 1.5,
            py: 0.75,
            mb: 0.75,
            flexWrap: 'wrap',
          }}
        >
          <Box
            component="code"
            sx={{
              bgcolor: 'action.selected',
              px: 0.5,
              borderRadius: '3px',
              fontFamily: 'monospace',
              fontSize: '0.8rem',
              flexShrink: 0,
            }}
          >
            {t.fieldName}
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
            →
          </Typography>
          <Typography variant="body2" sx={{ fontWeight: 500, flex: 1, minWidth: 80 }}>
            {t.name}
          </Typography>
          <Chip
            label={TRANSFORM_TYPE_LABELS[t.type]}
            size="small"
            color={typeChipColor(t.type)}
            variant="outlined"
            sx={{ flexShrink: 0 }}
          />
          <Chip label={summary(t)} size="small" variant="outlined" sx={{ flexShrink: 0 }} />
          <IconButton size="small" onClick={() => handleEdit(t)} title="Edit">
            <EditIcon fontSize="small" />
          </IconButton>
          <IconButton size="small" onClick={() => onRemove(t.id)} title="Delete">
            <DeleteIcon fontSize="small" />
          </IconButton>
        </Paper>
      ))}

      {/* Add form */}
      {showAddForm && (
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
              onChange={(e) => handleDraftFieldChange(e.target.value as string)}
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
                onClick={handleSaveDateTruncNew}
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
              onClick={handleOpenConfigure}
            >
              Configure
            </Button>
          )}

          <IconButton size="small" onClick={handleCancelAdd} title="Cancel">
            <CloseIcon fontSize="small" />
          </IconButton>
        </Paper>
      )}

      {/* NumericBin dialog */}
      {configTarget?.type === 'NumericBin' && (
        <NumericBinDialog
          open
          onClose={() => setConfigTarget(null)}
          onSave={handleSaveNumericBin}
          fieldName={configTarget.fieldName}
          initialBins={configTarget.bins}
          initialFallback={configTarget.fallback}
        />
      )}

      {/* StringGrouping dialog */}
      {configTarget?.type === 'StringGrouping' && (
        <StringGroupingDialog
          open
          onClose={() => setConfigTarget(null)}
          onSave={handleSaveStringGrouping}
          fieldName={configTarget.fieldName}
          initialGroups={configTarget.groups}
          initialFallback={configTarget.fallback}
          sourceId={sourceId}
          tableName={tableName}
        />
      )}

      {/* DateTrunc edit dialog */}
      {dateTruncEditTarget && (
        <Dialog open onClose={() => setDateTruncEditTarget(null)} maxWidth="xs" fullWidth>
          <DialogTitle>
            Edit DateTrunc —{' '}
            <Box
              component="code"
              sx={{
                bgcolor: 'action.selected',
                px: 0.5,
                borderRadius: '3px',
                fontFamily: 'monospace',
              }}
            >
              {dateTruncEditTarget.fieldName}
            </Box>
          </DialogTitle>
          <DialogContent dividers sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: 2 }}>
            <TextField
              size="small"
              fullWidth
              label="Name"
              value={dateTruncEditTarget.name}
              onChange={(e) => setDateTruncEditTarget((t) => t && { ...t, name: e.target.value })}
            />
            <FormControl size="small" fullWidth>
              <InputLabel>Granularity</InputLabel>
              <Select
                value={dateTruncEditTarget.granularity}
                label="Granularity"
                onChange={(e) =>
                  setDateTruncEditTarget(
                    (t) => t && { ...t, granularity: e.target.value as DateGranularity },
                  )
                }
              >
                {DATE_GRANULARITIES.map((g) => (
                  <MenuItem key={g} value={g}>
                    {g}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setDateTruncEditTarget(null)}>Cancel</Button>
            <Button variant="contained" onClick={handleSaveDateTruncEdit}>
              Save
            </Button>
          </DialogActions>
        </Dialog>
      )}
    </Box>
  );
}
