import { ToggleButton, ToggleButtonGroup } from '@mui/material';

import {
  allowedPermissions,
  PERMISSION_LABELS,
  ToolEffect,
  ToolPermission,
} from './toolPermission';

interface ToolPermissionToggleProps {
  /** The tool's name, for the group's label. */
  toolName: string;
  effect: ToolEffect;
  value: ToolPermission;
  onChange: (permission: ToolPermission) => void;
  disabled?: boolean;
}

/** One tool's permission as a three-way switch (two-way for a tool that cannot be undone). */
export default function ToolPermissionToggle({
  toolName,
  effect,
  value,
  onChange,
  disabled,
}: ToolPermissionToggleProps) {
  return (
    <ToggleButtonGroup
      size="small"
      exclusive
      value={value}
      disabled={disabled}
      onChange={(_, next: ToolPermission | null) => next && onChange(next)}
      aria-label={`${toolName} 的權限`}
      sx={{ '& .MuiToggleButton-root': { minHeight: 36, px: 1.5 } }}
    >
      {allowedPermissions(effect).map((p) => (
        <ToggleButton key={p} value={p}>
          {PERMISSION_LABELS[p]}
        </ToggleButton>
      ))}
    </ToggleButtonGroup>
  );
}
