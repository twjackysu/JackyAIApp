import AddIcon from '@mui/icons-material/Add';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';
import type { FieldTransformDef, ViewByEntry } from '../types';
import { ViewByRow } from './ViewByRow';

interface ViewByEditorProps {
  entries:    ViewByEntry[];
  fields:     FieldDefinition[];
  transforms: FieldTransformDef[];
  onAdd:      () => void;
  onUpdate:   (entry: ViewByEntry) => void;
  onRemove:   (id: number) => void;
}

export function ViewByEditor({
  entries, fields, transforms, onAdd, onUpdate, onRemove,
}: ViewByEditorProps) {
  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="subtitle2">Group By (ViewBy)</Typography>
        <IconButton size="small" onClick={onAdd} title="Add group field">
          <AddIcon fontSize="small" />
        </IconButton>
      </Box>

      {entries.length === 0 && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
          No group fields — click <strong>+</strong> to add one.
        </Typography>
      )}

      {entries.map((entry) => (
        <ViewByRow
          key={entry.id}
          entry={entry}
          fields={fields}
          transforms={transforms}
          onUpdate={onUpdate}
          onRemove={() => onRemove(entry.id)}
        />
      ))}
    </Box>
  );
}
