import ExtensionIcon from '@mui/icons-material/ExtensionOutlined';
import { Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useMemo } from 'react';
import { Link as RouterLink } from 'react-router-dom';

import { parseMcpServersCard, type McpServerFind } from './mcpServersCard';

import type { CustomCard } from '@/components/aiChat/cards/toolCard';

import { useGetConnectorCatalogQuery } from '@/apis/connectorsApis/connectorsApis';
import { ACCENT } from '@/components/aiChat/style';
import { connectorsRegistryPath } from '@/Connectors/AddMcpServerDialog/registryView';
import { installedServerUrls, serverUrlKey } from '@/Connectors/connectorView';

/**
 * Studio AI's finds in the official MCP Registry (`find_mcp_server`, D-124): each server's name, where it lives and
 * what it will ask for, with 加入, which opens /connectors with its add dialog searching for exactly that server.
 * Adding it is still the reader's own press there (the probe, the credentials, the tools' permissions): the card
 * only points, and what a publisher wrote is shown as plain text.
 */
export default function McpServersCardView({ card }: { card: CustomCard }) {
  const found = useMemo(() => parseMcpServersCard(card), [card]);
  // The reader's connectors, to show one already added as such (D-127); until they are read, every row offers 加入.
  const { data: catalog } = useGetConnectorCatalogQuery();
  const added = useMemo(() => installedServerUrls(catalog?.installed ?? []), [catalog]);
  if (!found) return null;
  return (
    <Box>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
        <ExtensionIcon sx={{ fontSize: 18, color: ACCENT }} />
        <Typography variant="subtitle2" sx={{ flex: 1 }}>
          MCP 官方目錄：{found.search}
        </Typography>
      </Stack>
      <Stack
        component="ul"
        spacing={1}
        aria-label={`MCP 官方目錄裡符合「${found.search}」的 server`}
        sx={{ listStyle: 'none', p: 0, m: 0 }}
      >
        {found.servers.map((s) => (
          <ServerRow key={s.name} server={s} added={added.has(serverUrlKey(s.url) ?? '')} />
        ))}
      </Stack>
      <Stack
        direction="row"
        sx={{
          alignItems: 'center',
          justifyContent: 'space-between',
          mt: 1,
          gap: 1,
          flexWrap: 'wrap',
        }}
      >
        <Typography variant="caption" color="text.secondary" sx={{ flex: 1, minWidth: 180 }}>
          內容由發布者提供；加入時你會看到它的工具並決定權限，只接你信任的 server。
        </Typography>
        <Button component={RouterLink} to={connectorsRegistryPath(found.search)} size="small">
          {found.more ? '看全部結果' : '在 Connectors 搜尋'}
        </Button>
      </Stack>
    </Box>
  );
}

function ServerRow({ server, added }: { server: McpServerFind; added: boolean }) {
  return (
    <Box
      component="li"
      sx={{
        border: 1,
        borderColor: 'divider',
        borderRadius: 1,
        p: 1.25,
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
        {server.host && (
          <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
            {server.host}
          </Typography>
        )}
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
        {server.needs && (
          <Box>
            <Chip label={server.needs} size="small" variant="outlined" sx={{ mt: 0.5 }} />
          </Box>
        )}
      </Stack>
      {added ? (
        <Button
          component={RouterLink}
          to="/connectors"
          size="small"
          aria-label={`${server.title} 已經加入了，到 Connectors 查看`}
          sx={{ flexShrink: 0 }}
        >
          已加入
        </Button>
      ) : (
        <Button
          component={RouterLink}
          to={server.inCatalog ? '/connectors' : connectorsRegistryPath(server.name)}
          size="small"
          variant="outlined"
          aria-label={`到 Connectors 加入 ${server.title}`}
          sx={{ flexShrink: 0 }}
        >
          {server.inCatalog ? '從目錄連線' : '加入'}
        </Button>
      )}
    </Box>
  );
}
