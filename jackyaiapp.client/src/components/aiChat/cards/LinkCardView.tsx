import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { Box, Button, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';

import type { LinkCard } from './toolCard';

/** Somewhere the answer points to: a page on this site (same tab), or https (a new one). */
export default function LinkCardView({ card }: { card: LinkCard }) {
  const internal = card.href.startsWith('/');
  const label = card.actionLabel ?? (internal ? '前往' : '開啟');
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
      <Box sx={{ flex: 1, minWidth: 160 }}>
        <Typography variant="subtitle2">{card.title}</Typography>
        {card.description && (
          <Typography variant="body2" color="text.secondary">
            {card.description}
          </Typography>
        )}
      </Box>
      {internal ? (
        <Button component={RouterLink} to={card.href} variant="outlined" size="small">
          {label}
        </Button>
      ) : (
        <Button
          href={card.href}
          target="_blank"
          rel="noopener noreferrer"
          variant="outlined"
          size="small"
          endIcon={<OpenInNewIcon fontSize="small" />}
        >
          {label}
        </Button>
      )}
    </Box>
  );
}
