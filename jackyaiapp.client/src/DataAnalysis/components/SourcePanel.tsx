import SettingsIcon from '@mui/icons-material/Settings';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import type { DataSourceConfigDto } from '../../apis/dataAnalysisApis/types';

interface SourcePanelProps {
  isLoggedIn: boolean;
  sources: DataSourceConfigDto[];
  activeSourceId: string | null;
  tableNameInput: string;
  onSourceChange: (id: string) => void;
  onTableNameChange: (val: string) => void;
  onApplyTableName: () => void;
  onOpenManager: () => void;
}

export function SourcePanel({
  isLoggedIn,
  sources,
  activeSourceId,
  tableNameInput,
  onSourceChange,
  onTableNameChange,
  onApplyTableName,
  onOpenManager,
}: SourcePanelProps) {
  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
      <Typography variant="subtitle2" gutterBottom sx={{ color: 'text.secondary' }}>
        Data Source
      </Typography>

      {isLoggedIn ? (
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <FormControl size="small" sx={{ minWidth: 260 }}>
            <InputLabel>Source</InputLabel>
            <Select
              value={activeSourceId ?? ''}
              label="Source"
              onChange={(e) => onSourceChange(e.target.value as string)}
            >
              <MenuItem value="">
                <em>Default (JackyAI SQL / dbo.Orders)</em>
              </MenuItem>
              {sources.map((s) => (
                <MenuItem key={s.id} value={s.id}>
                  {s.name}&nbsp;
                  <Chip
                    label={s.connectorType} size="small"
                    color={s.connectorType === 'Adx' ? 'secondary' : 'primary'}
                    sx={{ ml: 0.5 }}
                  />
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <TextField
            size="small" label="Table"
            value={tableNameInput}
            onChange={(e) => onTableNameChange(e.target.value)}
            onBlur={onApplyTableName}
            onKeyDown={(e) => { if (e.key === 'Enter') onApplyTableName(); }}
            sx={{ width: 220 }}
            helperText="Press Enter or click away to apply"
          />

          <Button
            size="small" variant="outlined"
            startIcon={<SettingsIcon />}
            onClick={onOpenManager}
            sx={{ mt: 0.25 }}
          >
            Manage Sources
          </Button>
        </Box>
      ) : (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Chip label="Default (JackyAI SQL / dbo.Orders)" color="default" size="small" />
          <Typography variant="body2" color="text.secondary">
            —{' '}
            <a
              href={`/api/account/login/Google?ReturnUrl=${encodeURIComponent(window.location.pathname)}`}
              style={{ color: 'inherit' }}
            >
              Sign in
            </a>
            {' '}to configure and use custom data sources.
          </Typography>
        </Box>
      )}
    </Paper>
  );
}
