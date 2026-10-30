import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { Link as RouterLink } from 'react-router-dom';

import { MAX_NAMED_CONNECTORS, usableConnectors } from './usableConnectors';

import { useGetConnectorCatalogQuery } from '@/apis/connectorsApis/connectorsApis';
import CategoryIcon from '@/Connectors/CategoryIcon';

/**
 * Under Studio AI's welcome (design/05 §1): the connectors it can use now — the site's own and those the reader added —
 * so a new conversation shows what it can reach before anything is asked, and where to add more. Nothing while the list
 * is read or when it cannot be.
 */
export default function StudioToolsRow() {
  const { data: catalog } = useGetConnectorCatalogQuery();
  if (!catalog) return null;
  const usable = usableConnectors(catalog.installed);
  const named = usable.slice(0, MAX_NAMED_CONNECTORS);
  const more = usable.length - named.length;

  return (
    <Box data-testid="studio-tools" sx={{ mt: 2, width: '100%', maxWidth: 560 }}>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 0.75 }}>
        我能用的工具
      </Typography>
      <Box
        component="ul"
        sx={{
          listStyle: 'none',
          m: 0,
          p: 0,
          display: 'flex',
          flexWrap: 'wrap',
          justifyContent: 'center',
          alignItems: 'center',
          gap: 0.75,
        }}
      >
        {named.map((c) => (
          <li key={c.id ?? c.definitionKey}>
            <Chip
              size="small"
              variant="outlined"
              icon={<CategoryIcon category={c.category} />}
              label={c.displayName}
            />
          </li>
        ))}
        {more > 0 && (
          <li>
            <Typography variant="caption" color="text.secondary">
              還有 {more} 個
            </Typography>
          </li>
        )}
        <li>
          <Link component={RouterLink} to="/connectors" variant="caption" sx={{ ml: 0.5 }}>
            {usable.some((c) => !c.alwaysOn) ? '管理 connectors' : '接上更多工具'}
          </Link>
        </li>
      </Box>
    </Box>
  );
}
