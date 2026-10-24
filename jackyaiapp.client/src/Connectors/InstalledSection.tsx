import {
  Box,
  Card,
  CardActionArea,
  CardContent,
  Chip,
  Grid,
  Stack,
  Typography,
} from '@mui/material';

import CategoryIcon from './CategoryIcon';
import { HEALTH_VIEW, installedOrder, toolCounts } from './connectorView';
import { PERMISSION_LABELS } from './toolPermission';

import type { InstalledConnector } from '@/apis/connectorsApis/types';

interface InstalledSectionProps {
  connectors: InstalledConnector[];
  onOpen: (connector: InstalledConnector) => void;
}

/** The connectors the AI can use now, each opening its details (tools, permissions, test). */
export default function InstalledSection({ connectors, onOpen }: InstalledSectionProps) {
  return (
    <Box component="section" aria-labelledby="installed-title" sx={{ mb: 5 }}>
      <Typography id="installed-title" variant="h6" component="h2" gutterBottom>
        AI 可以使用的
      </Typography>
      {connectors.length === 0 ? (
        <Typography color="text.secondary">
          還沒有連線的 connector。從下方目錄挑一個，或新增你自己的 MCP server。
        </Typography>
      ) : (
        <Grid container spacing={2}>
          {installedOrder(connectors).map((c) => (
            <Grid key={c.id ?? c.definitionKey} size={{ xs: 12, sm: 6, md: 4 }}>
              <InstalledCard connector={c} onOpen={() => onOpen(c)} />
            </Grid>
          ))}
        </Grid>
      )}
    </Box>
  );
}

function InstalledCard({
  connector,
  onOpen,
}: {
  connector: InstalledConnector;
  onOpen: () => void;
}) {
  const health = HEALTH_VIEW[connector.health];
  const counts = toolCounts(connector);
  return (
    <Card variant="outlined" sx={{ height: '100%' }}>
      <CardActionArea onClick={onOpen} sx={{ height: '100%' }}>
        <CardContent>
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 1 }}>
            <CategoryIcon category={connector.category} color="primary" />
            <Typography variant="subtitle1" component="h3" sx={{ flex: 1, fontWeight: 600 }}>
              {connector.displayName}
            </Typography>
            <Chip
              size="small"
              label={connector.alwaysOn ? '內建' : health.label}
              color={connector.alwaysOn ? 'primary' : health.color}
              variant={connector.alwaysOn ? 'outlined' : 'filled'}
            />
          </Stack>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {connector.description}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {connector.tools.length} 個工具：
            {(['auto', 'confirm', 'disabled'] as const)
              .filter((p) => counts[p] > 0)
              .map((p) => `${PERMISSION_LABELS[p]} ${counts[p]}`)
              .join('・') || '尚未讀到工具'}
          </Typography>
        </CardContent>
      </CardActionArea>
    </Card>
  );
}
