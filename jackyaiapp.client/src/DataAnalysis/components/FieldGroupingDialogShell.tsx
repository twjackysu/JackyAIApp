import AddIcon from '@mui/icons-material/Add';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import Divider from '@mui/material/Divider';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import { FieldDialogTitle } from './FieldDialogTitle';

import type { ReactNode } from 'react';

interface FieldGroupingDialogShellProps {
  open: boolean;
  onClose: () => void;
  /** What the dialog does, e.g. 「數值分組 (Numeric Bins)」. */
  title: string;
  /** The column being grouped. */
  fieldName: string;
  /** One-paragraph explanation of the grouping rules, shown above the entries. */
  hint: ReactNode;
  /** The entry editors themselves — bins, groups, whatever the concrete dialog edits. */
  children: ReactNode;
  /** Label of the "add another entry" button, e.g. 「新增區間」. */
  addLabel: string;
  onAdd: () => void;
  /** Optional notice rendered under the add button, above the fallback field. */
  belowAdd?: ReactNode;
  /** Label of the fallback field — the two dialogs word it differently. */
  fallbackLabel: string;
  fallback: string;
  onFallbackChange: (value: string) => void;
  /** Blocks 套用 while the entries are invalid; the concrete dialog owns the validation. */
  hasErrors: boolean;
  onApply: () => void;
}

/**
 * Chrome shared by the field-grouping dialogs (numeric bins, string groups): the field-scoped
 * title, the hint paragraph, the entries, an "add entry" button, and the fallback label plus
 * 取消/套用 footer. Only the entry editors and their validation differ between the two, so
 * everything around them lives here rather than being kept in sync by hand.
 *
 * 套用 stays disabled until the entries validate, and says so in its own label — a disabled
 * button gets neither hover nor tap, so the reason can't hide in a tooltip (see the frontend
 * CLAUDE.md).
 */
export function FieldGroupingDialogShell({
  open,
  onClose,
  title,
  fieldName,
  hint,
  children,
  addLabel,
  onAdd,
  belowAdd,
  fallbackLabel,
  fallback,
  onFallbackChange,
  hasErrors,
  onApply,
}: FieldGroupingDialogShellProps) {
  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <FieldDialogTitle fieldName={fieldName}>{title}</FieldDialogTitle>

      <DialogContent dividers>
        <Typography variant="caption" color="text.secondary" sx={{ mb: 2, display: 'block' }}>
          {hint}
        </Typography>

        {children}

        <Button size="small" startIcon={<AddIcon />} onClick={onAdd} sx={{ mt: 0.5 }}>
          {addLabel}
        </Button>

        {belowAdd}

        <Divider sx={{ my: 2 }} />

        <TextField
          size="small"
          fullWidth
          label={fallbackLabel}
          value={fallback}
          onChange={(e) => onFallbackChange(e.target.value)}
        />
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose}>取消</Button>
        <Button variant="contained" disabled={hasErrors} onClick={onApply}>
          套用{hasErrors ? '（請先修正錯誤）' : ''}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
