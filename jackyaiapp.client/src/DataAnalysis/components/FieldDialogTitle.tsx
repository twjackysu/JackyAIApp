import Box from '@mui/material/Box';
import DialogTitle from '@mui/material/DialogTitle';

import type { ReactNode } from 'react';

interface FieldDialogTitleProps {
  /** What the dialog does, e.g. 「數值分組 (Numeric Bins)」. */
  children: ReactNode;
  /** The column the dialog operates on — rendered as inline code after the title. */
  fieldName: string;
}

/**
 * Title row shared by every dialog that edits one field's definition: what the dialog does,
 * then the field name in a monospace chip so it stays readable next to Chinese copy.
 */
export function FieldDialogTitle({ children, fieldName }: FieldDialogTitleProps) {
  return (
    <DialogTitle>
      {children} —{' '}
      <Box
        component="code"
        sx={{ bgcolor: 'action.selected', px: 0.5, borderRadius: '3px', fontFamily: 'monospace' }}
      >
        {fieldName}
      </Box>
    </DialogTitle>
  );
}
