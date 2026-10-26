import { Search } from '@mui/icons-material';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  InputAdornment,
  Link,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useEffect, useState } from 'react';

import { serverUrlKey } from '../connectorView';

import {
  REGISTRY_SEARCH_DELAY_MS,
  registryHost,
  registryNeeds,
  registryNotice,
} from './registryView';

import type { McpRegistryServer } from '@/apis/connectorsApis/types';

import { useSearchMcpRegistryQuery } from '@/apis/connectorsApis/connectorsApis';
import { getApiErrorMessage } from '@/apis/utils/errorMessage';

interface RegistrySearchProps {
  /** The reader chose one: the dialog checks its address as if it had been pasted. */
  onPick: (server: McpRegistryServer) => void;
  /** A pick is being checked, so the buttons wait. */
  busy: boolean;
  /** Opened to search (the catalog's 找更多): the search box takes the focus. */
  autoFocus?: boolean;
  /** What the search starts with (Studio AI's card sends the reader here with a search, D-124). */
  initialSearch?: string;
  /** The servers the reader has added (`installedServerUrls`): shown as added, not offered again. */
  installedUrls?: ReadonlySet<string>;
}

const NONE: ReadonlySet<string> = new Set();

/** Only an https link someone published is followed (the server keeps only those, too). */
const safeLink = (url: string | null) => (url?.startsWith('https://') ? url : null);

/**
 * Finding a remote MCP server in the official MCP Registry (D-122): the registry matches names
 * only, so the search is a few English letters; each server shows where it lives and what it
 * will ask for, and choosing one checks its address the way a pasted one is checked.
 */
export default function RegistrySearch({
  onPick,
  busy,
  autoFocus = false,
  initialSearch = '',
  installedUrls = NONE,
}: RegistrySearchProps) {
  const [typed, setTyped] = useState(initialSearch);
  const [search, setSearch] = useState(initialSearch.trim());
  useEffect(() => {
    const timer = setTimeout(() => setSearch(typed.trim()), REGISTRY_SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [typed]);

  // currentData, not data: while a new search loads, the last one's answer is not shown as its own.
  const { currentData: data, error, isFetching } = useSearchMcpRegistryQuery(search);
  const notice = data && !error ? registryNotice(data, search) : null;

  return (
    <Stack spacing={1.5}>
      <TextField
        label="搜尋 MCP 官方目錄"
        placeholder="例如 github、weather、search"
        value={typed}
        onChange={(e) => setTyped(e.target.value)}
        size="small"
        autoFocus={autoFocus}
        fullWidth
        slotProps={{
          htmlInput: { maxLength: 50, 'aria-describedby': 'registry-hint' },
          input: {
            startAdornment: (
              <InputAdornment position="start">
                <Search fontSize="small" />
              </InputAdornment>
            ),
            endAdornment: isFetching ? (
              <InputAdornment position="end">
                <CircularProgress size={16} aria-label="搜尋中" />
              </InputAdornment>
            ) : undefined,
          },
        }}
      />
      <Typography id="registry-hint" variant="caption" color="text.secondary">
        列出 registry.modelcontextprotocol.io 上可以直接連線的 remote MCP
        server（比對名稱與說明）。內容由發布者提供，連線前請確認你信任它。
      </Typography>

      {error && <Alert severity="warning">{getApiErrorMessage(error)}</Alert>}

      {data && !error && (
        <Stack
          component="ul"
          spacing={1}
          aria-label="MCP 官方目錄的搜尋結果"
          sx={{ listStyle: 'none', p: 0, m: 0, maxHeight: 340, overflowY: 'auto' }}
        >
          {data.servers.map((s) => (
            <RegistryRow
              key={s.url}
              server={s}
              busy={busy}
              added={installedUrls.has(serverUrlKey(s.url) ?? '')}
              onPick={onPick}
            />
          ))}
        </Stack>
      )}
      {notice && (
        <Typography variant="caption" color="text.secondary" role="status">
          {notice}
        </Typography>
      )}
    </Stack>
  );
}

function RegistryRow({
  server,
  busy,
  added,
  onPick,
}: {
  server: McpRegistryServer;
  busy: boolean;
  added: boolean;
  onPick: (server: McpRegistryServer) => void;
}) {
  const needs = registryNeeds(server);
  const link = safeLink(server.repositoryUrl) ?? safeLink(server.websiteUrl);
  return (
    <Box
      component="li"
      sx={{
        border: 1,
        borderColor: 'divider',
        borderRadius: 1,
        p: 1.5,
        display: 'flex',
        flexDirection: { xs: 'column', sm: 'row' },
        gap: 1,
        alignItems: { xs: 'stretch', sm: 'center' },
      }}
    >
      <Stack sx={{ flex: 1, minWidth: 0 }} spacing={0.25}>
        <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
          {server.title}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
          {registryHost(server)}
          {server.version ? ` · v${server.version}` : ''}
          {link && (
            <>
              {' · '}
              <Link href={link} target="_blank" rel="noopener noreferrer">
                原始碼／網站
              </Link>
            </>
          )}
        </Typography>
        {server.description && (
          <Typography
            variant="caption"
            sx={{
              display: '-webkit-box',
              WebkitLineClamp: 2,
              WebkitBoxOrient: 'vertical',
              overflow: 'hidden',
              overflowWrap: 'anywhere',
            }}
          >
            {server.description}
          </Typography>
        )}
        {needs && (
          <Box>
            <Chip label={needs} size="small" variant="outlined" sx={{ mt: 0.5 }} />
          </Box>
        )}
      </Stack>
      {added ? (
        <Typography variant="caption" color="text.secondary" sx={{ flexShrink: 0 }}>
          已經加入了
        </Typography>
      ) : server.catalogKey ? (
        <Typography variant="caption" color="text.secondary" sx={{ flexShrink: 0 }}>
          上方目錄已有，從那裡連線
        </Typography>
      ) : (
        <Button
          size="small"
          variant="outlined"
          disabled={busy}
          onClick={() => onPick(server)}
          aria-label={`使用 ${server.title}`}
          sx={{ flexShrink: 0 }}
        >
          使用
        </Button>
      )}
    </Box>
  );
}
