import AddIcon from '@mui/icons-material/Add';
import TransformIcon from '@mui/icons-material/Transform';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { AddTransformForm } from './fieldTransform/AddTransformForm';
import { DateTruncEditDialog } from './fieldTransform/DateTruncEditDialog';
import {
  effectiveName,
  emptyDraft,
  inferTransformType,
  type DraftState,
} from './fieldTransform/helpers';
import { TransformListItem } from './fieldTransform/TransformListItem';
import { NumericBinDialog } from './NumericBinDialog';
import { StringGroupingDialog } from './StringGroupingDialog';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { BinEntry, FieldTransformDef, GroupEntry } from '../types';

import { ActionTooltip } from '@/components/hints';

interface FieldTransformEditorProps {
  transforms: FieldTransformDef[];
  onAdd: (t: Omit<FieldTransformDef, 'id'>) => void;
  onUpdate: (t: FieldTransformDef) => void;
  onRemove: (id: number) => void;
  fields: FieldDefinition[];
  sourceId?: string;
  tableName?: string;
}

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

  // Field selection: auto-derive transform type from the field's canonical type.
  const handleDraftFieldChange = (fieldName: string) => {
    const canonicalType = fields.find((f) => f.name === fieldName)?.canonicalType ?? 'String';
    setDraft((d) => ({ ...d, fieldName, type: inferTransformType(canonicalType) }));
  };

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

  const handleEdit = (t: FieldTransformDef) => {
    if (t.type === 'DateTrunc') setDateTruncEditTarget({ ...t });
    else setConfigTarget({ ...t });
  };

  const handleSaveDateTruncEdit = () => {
    if (dateTruncEditTarget) onUpdate(dateTruncEditTarget);
    setDateTruncEditTarget(null);
  };

  return (
    <Box>
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <TransformIcon fontSize="small" color="action" />
          <Typography variant="subtitle2">
            欄位轉換 (Field Transforms)
            {transforms.length > 0 && (
              <Chip label={transforms.length} size="small" sx={{ ml: 1 }} />
            )}
          </Typography>
        </Box>
        {!showAddForm && (
          <ActionTooltip title="新增轉換">
            <IconButton size="small" onClick={() => setShowAddForm(true)}>
              <AddIcon fontSize="small" />
            </IconButton>
          </ActionTooltip>
        )}
      </Box>

      {/* Empty state */}
      {transforms.length === 0 && !showAddForm && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
          尚無轉換 — 點擊 <strong>+</strong>{' '}
          建立一個。轉換用來定義欄位值如何被歸類或重新命名，可套用在分組欄位（Group
          By）與篩選條件（Filters）。
        </Typography>
      )}

      {/* Existing transform list */}
      {transforms.map((t) => (
        <TransformListItem key={t.id} transform={t} onEdit={handleEdit} onRemove={onRemove} />
      ))}

      {/* Add form */}
      {showAddForm && (
        <AddTransformForm
          fields={fields}
          draft={draft}
          setDraft={setDraft}
          onFieldChange={handleDraftFieldChange}
          onSaveDateTrunc={handleSaveDateTruncNew}
          onConfigure={handleOpenConfigure}
          onCancel={handleCancelAdd}
        />
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
        <DateTruncEditDialog
          target={dateTruncEditTarget}
          onChange={setDateTruncEditTarget}
          onClose={() => setDateTruncEditTarget(null)}
          onSave={handleSaveDateTruncEdit}
        />
      )}
    </Box>
  );
}
