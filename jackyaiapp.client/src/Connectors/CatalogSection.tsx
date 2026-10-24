import {
  Box,
  Button,
  Card,
  CardActions,
  CardContent,
  Chip,
  Grid,
  Stack,
  Typography,
} from '@mui/material';
import { useState } from 'react';

import CategoryIcon from './CategoryIcon';
import { CATEGORY_LABELS, catalogCategories, catalogEntries, connectAction } from './connectorView';

import type { CatalogEntry, ConnectorCategory } from '@/apis/connectorsApis/types';

interface CatalogSectionProps {
  entries: CatalogEntry[];
  /** The entry whose connect request is on its way, so its button waits. */
  busyKey: string | null;
  onConnect: (entry: CatalogEntry) => void;
}

const ACTION_LABELS = {
  installed: '已加入',
  install: '連線',
  askCredentials: '連線…',
  authorize: '前往授權',
} as const;

/** Ready-made connectors, filtered by category; connecting one is a click (or a sign-in). */
export default function CatalogSection({ entries, busyKey, onConnect }: CatalogSectionProps) {
  const [category, setCategory] = useState<ConnectorCategory | null>(null);
  const shown = catalogEntries(entries, category);
  const categories = catalogCategories(entries);

  return (
    <Box component="section" aria-labelledby="catalog-title" sx={{ mb: 5 }}>
      <Typography id="catalog-title" variant="h6" component="h2" gutterBottom>
        目錄
      </Typography>
      {categories.length > 1 && (
        <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: 'wrap', rowGap: 1 }}>
          <Chip
            label="全部"
            color={category === null ? 'primary' : 'default'}
            onClick={() => setCategory(null)}
          />
          {categories.map((c) => (
            <Chip
              key={c}
              label={CATEGORY_LABELS[c]}
              color={category === c ? 'primary' : 'default'}
              onClick={() => setCategory(c)}
            />
          ))}
        </Stack>
      )}
      <Grid container spacing={2}>
        {shown.map((e) => {
          const action = connectAction(e);
          return (
            <Grid key={e.key} size={{ xs: 12, sm: 6, md: 4 }}>
              <Card
                variant="outlined"
                sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}
              >
                <CardContent sx={{ flex: 1 }}>
                  <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 1 }}>
                    <CategoryIcon category={e.category} color="action" />
                    <Typography variant="subtitle1" component="h3" sx={{ fontWeight: 600 }}>
                      {e.displayName}
                    </Typography>
                  </Stack>
                  <Typography variant="body2" color="text.secondary">
                    {e.description}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {CATEGORY_LABELS[e.category]}・{e.publisher}
                  </Typography>
                </CardContent>
                <CardActions>
                  <Button
                    variant={action === 'installed' ? 'text' : 'contained'}
                    disabled={action === 'installed' || busyKey === e.key}
                    onClick={() => onConnect(e)}
                  >
                    {ACTION_LABELS[action]}
                  </Button>
                  {e.docsUrl && (
                    <Button href={e.docsUrl} target="_blank" rel="noopener noreferrer">
                      說明
                    </Button>
                  )}
                </CardActions>
              </Card>
            </Grid>
          );
        })}
      </Grid>
    </Box>
  );
}
