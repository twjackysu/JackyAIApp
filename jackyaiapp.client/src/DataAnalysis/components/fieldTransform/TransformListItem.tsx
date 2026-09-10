import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';

import { TRANSFORM_TYPE_LABELS } from '../../constants';

import { summary, typeChipColor } from './helpers';

import type { FieldTransformDef } from '../../types';

import { ActionTooltip } from '@/components/hints';

interface TransformListItemProps {
  transform: FieldTransformDef;
  onEdit: (t: FieldTransformDef) => void;
  onRemove: (id: number) => void;
}

export function TransformListItem({ transform: t, onEdit, onRemove }: TransformListItemProps) {
  return (
    <Paper
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
      <ActionTooltip title="編輯">
        <IconButton size="small" onClick={() => onEdit(t)}>
          <EditIcon fontSize="small" />
        </IconButton>
      </ActionTooltip>
      <ActionTooltip title="刪除">
        <IconButton size="small" onClick={() => onRemove(t.id)}>
          <DeleteIcon fontSize="small" />
        </IconButton>
      </ActionTooltip>
    </Paper>
  );
}
