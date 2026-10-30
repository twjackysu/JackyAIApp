import { Button, Stack, TextField } from '@mui/material';
import { useState } from 'react';

import type { InstalledConnector } from '@/apis/connectorsApis/types';

interface CredentialsFormProps {
  authKind: Extract<InstalledConnector['authKind'], 'apiKey' | 'bearer'>;
  busy: boolean;
  /**
   * Saves and tests the new key or token; the form closes once it has been tried. An API key's header is given only
   * when the reader typed one: left empty, the key goes in the header it went in.
   */
  onSave: (value: string, headerName?: string) => Promise<void>;
  onCancel: () => void;
}

/**
 * A new API key or bearer token for a connector that signs in with one (D-224): what its server refused is replaced
 * here, in place, rather than by removing the connector and adding it again. An API key keeps the header it went in,
 * unless the reader names another — which they must when the stored credentials can no longer be read (D-238).
 */
export default function CredentialsForm({
  authKind,
  busy,
  onSave,
  onCancel,
}: CredentialsFormProps) {
  const [value, setValue] = useState('');
  const [headerName, setHeaderName] = useState('');
  const label = authKind === 'apiKey' ? '新的 API key' : '新的 token';
  const save = async () => {
    if (!value.trim()) return;
    const header = authKind === 'apiKey' ? headerName.trim() : '';
    await (header ? onSave(value.trim(), header) : onSave(value.trim()));
  };

  return (
    <Stack
      component="form"
      direction={{ xs: 'column', sm: 'row' }}
      spacing={1}
      sx={{ mb: 3, alignItems: { xs: 'stretch', sm: 'center' } }}
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
    >
      <TextField
        size="small"
        type="password"
        label={label}
        value={value}
        autoFocus
        autoComplete="off"
        onChange={(e) => setValue(e.target.value)}
        sx={{ flex: 1 }}
      />
      {authKind === 'apiKey' && (
        <TextField
          size="small"
          label="Header 名稱（選填）"
          placeholder="留空沿用原本的"
          value={headerName}
          autoComplete="off"
          onChange={(e) => setHeaderName(e.target.value)}
          sx={{ flex: { sm: '0 0 200px' } }}
        />
      )}
      <Stack direction="row" spacing={1}>
        <Button type="submit" variant="contained" disabled={busy || !value.trim()}>
          儲存並測試
        </Button>
        <Button onClick={onCancel}>取消</Button>
      </Stack>
    </Stack>
  );
}
