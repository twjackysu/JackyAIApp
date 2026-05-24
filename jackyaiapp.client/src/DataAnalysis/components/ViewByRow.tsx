import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import IconButton from '@mui/material/IconButton';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import { useState } from 'react';

import type { DateGranularity, FieldDefinition, GroupFieldType } from '../../apis/dataAnalysisApis/types';
import { DATE_GRANULARITIES, GROUP_FIELD_TYPE_LABELS } from '../constants';
import type { ViewByEntry } from '../types';
import { NumericBinDialog } from './NumericBinDialog';
import { StringGroupingDialog } from './StringGroupingDialog';

interface ViewByRowProps {
  entry: ViewByEntry;
  fields: FieldDefinition[];
  onUpdate: (e: ViewByEntry) => void;
  onRemove: () => void;
  sourceId?: string;
  tableName?: string;
}

export function ViewByRow({ entry, fields, onUpdate, onRemove, sourceId, tableName }: ViewByRowProps) {
  const [binsOpen, setBinsOpen]     = useState(false);
  const [groupsOpen, setGroupsOpen] = useState(false);

  return (
    <Box sx={{ display: 'flex', gap: 1, mb: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {/* Field selector */}
      <FormControl size="small" sx={{ flex: '0 0 160px' }}>
        <InputLabel>Field</InputLabel>
        <Select
          value={entry.fieldName} label="Field"
          onChange={(e) => onUpdate({ ...entry, fieldName: e.target.value as string })}
        >
          {fields.map((f) => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}
        </Select>
      </FormControl>

      {/* Type selector */}
      <FormControl size="small" sx={{ flex: '0 0 140px' }}>
        <InputLabel>Type</InputLabel>
        <Select
          value={entry.type} label="Type"
          onChange={(e) => onUpdate({ ...entry, type: e.target.value as GroupFieldType })}
        >
          {(Object.keys(GROUP_FIELD_TYPE_LABELS) as GroupFieldType[]).map((t) => (
            <MenuItem key={t} value={t}>{GROUP_FIELD_TYPE_LABELS[t]}</MenuItem>
          ))}
        </Select>
      </FormControl>

      {/* Type-specific transformation controls */}
      {entry.type === 'DateTrunc' && (
        <FormControl size="small" sx={{ flex: '0 0 130px' }}>
          <InputLabel>Granularity</InputLabel>
          <Select
            value={entry.granularity} label="Granularity"
            onChange={(e) => onUpdate({ ...entry, granularity: e.target.value as DateGranularity })}
          >
            {DATE_GRANULARITIES.map((g) => <MenuItem key={g} value={g}>{g}</MenuItem>)}
          </Select>
        </FormControl>
      )}

      {entry.type === 'NumericBin' && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 0.25 }}>
          {entry.bins.length > 0 && (
            <Chip
              label={`${entry.bins.length} bin${entry.bins.length > 1 ? 's' : ''}`}
              size="small" color="primary"
            />
          )}
          <Button
            size="small" variant="outlined"
            startIcon={<EditIcon fontSize="small" />}
            onClick={() => setBinsOpen(true)}
          >
            {entry.bins.length === 0 ? 'Configure Bins' : 'Edit Bins'}
          </Button>
          <NumericBinDialog
            open={binsOpen}
            onClose={() => setBinsOpen(false)}
            onSave={(bins, fallback) => {
              onUpdate({ ...entry, bins, fallback });
              setBinsOpen(false);
            }}
            fieldName={entry.fieldName}
            initialBins={entry.bins}
            initialFallback={entry.fallback}
          />
        </Box>
      )}

      {entry.type === 'StringGrouping' && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 0.25 }}>
          {entry.groups.length > 0 && (
            <Chip
              label={`${entry.groups.length} group${entry.groups.length > 1 ? 's' : ''}`}
              size="small" color="secondary"
            />
          )}
          <Button
            size="small" variant="outlined"
            startIcon={<EditIcon fontSize="small" />}
            onClick={() => setGroupsOpen(true)}
          >
            {entry.groups.length === 0 ? 'Configure Groups' : 'Edit Groups'}
          </Button>
          <StringGroupingDialog
            open={groupsOpen}
            onClose={() => setGroupsOpen(false)}
            onSave={(groups, fallback) => {
              onUpdate({ ...entry, groups, fallback });
              setGroupsOpen(false);
            }}
            fieldName={entry.fieldName}
            initialGroups={entry.groups}
            initialFallback={entry.fallback}
            sourceId={sourceId}
            tableName={tableName}
          />
        </Box>
      )}

      {/* Alias — available on all types; renames the output column in results */}
      <TextField
        size="small" label="Alias" placeholder="output col name"
        value={entry.alias}
        sx={{ flex: '0 0 140px' }}
        onChange={(e) => onUpdate({ ...entry, alias: e.target.value })}
      />

      <IconButton size="small" onClick={onRemove} title="Remove" sx={{ mt: 0.25 }}>
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
