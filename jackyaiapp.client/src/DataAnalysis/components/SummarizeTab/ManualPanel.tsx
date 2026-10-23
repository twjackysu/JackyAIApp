import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';

import { FieldTransformEditor } from '../FieldTransformEditor';
import { FilterEditor } from '../FilterEditor';
import { MeasureEditor } from '../MeasureEditor';
import { ViewByEditor } from '../ViewByEditor';

import type { SummarizeTabProps } from './types';

type ManualPanelProps = Pick<
  SummarizeTabProps,
  | 'fieldTransforms'
  | 'onTransformAdd'
  | 'onTransformUpdate'
  | 'onTransformRemove'
  | 'viewByEntries'
  | 'onViewByAdd'
  | 'onViewByUpdate'
  | 'onViewByRemove'
  | 'measures'
  | 'onMeasureAdd'
  | 'onMeasureRemove'
  | 'onMeasureUpdate'
  | 'summarizeFilters'
  | 'onSummarizeFiltersChange'
  | 'onRunSummarize'
  | 'isLoading'
  | 'fields'
  | 'sourceId'
  | 'tableName'
>;

/** Manual mode: the transforms, group-bys, measures and filters of a query, and its run button. */
export function ManualPanel({
  fieldTransforms,
  onTransformAdd,
  onTransformUpdate,
  onTransformRemove,
  viewByEntries,
  onViewByAdd,
  onViewByUpdate,
  onViewByRemove,
  measures,
  onMeasureAdd,
  onMeasureRemove,
  onMeasureUpdate,
  summarizeFilters,
  onSummarizeFiltersChange,
  onRunSummarize,
  isLoading,
  fields,
  sourceId,
  tableName,
}: ManualPanelProps) {
  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
      <FieldTransformEditor
        transforms={fieldTransforms}
        onAdd={onTransformAdd}
        onUpdate={onTransformUpdate}
        onRemove={onTransformRemove}
        fields={fields}
        sourceId={sourceId}
        tableName={tableName}
      />

      <Divider sx={{ my: 2 }} />

      <ViewByEditor
        entries={viewByEntries}
        fields={fields}
        transforms={fieldTransforms}
        onAdd={onViewByAdd}
        onUpdate={onViewByUpdate}
        onRemove={onViewByRemove}
      />

      <Divider sx={{ my: 2 }} />

      <MeasureEditor
        measures={measures}
        fields={fields}
        onAdd={onMeasureAdd}
        onRemove={onMeasureRemove}
        onUpdate={onMeasureUpdate}
      />

      <Divider sx={{ my: 2 }} />

      <FilterEditor
        filters={summarizeFilters}
        fields={fields}
        transforms={fieldTransforms}
        onChange={onSummarizeFiltersChange}
        sourceId={sourceId}
        tableName={tableName}
      />

      <Button
        variant="contained"
        startIcon={<PlayArrowIcon />}
        onClick={onRunSummarize}
        disabled={isLoading || viewByEntries.length === 0 || measures.length === 0}
        sx={{ mt: 2 }}
      >
        {isLoading ? '執行中…' : '執行查詢'}
      </Button>
    </Paper>
  );
}
