import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import Typography from '@mui/material/Typography';

import { askStudio } from './askStudio';

import type { ReactNode } from 'react';

import { ACCENT_GRADIENT } from '@/components/aiChat/style';

export interface AskStudioCardProps {
  icon: ReactNode;
  title: string;
  description: string;
  /** What Studio AI is asked when the card is pressed, always as a new conversation: it stands on its own. */
  ask: string;
}

/**
 * A page's one-press way into Studio AI (the home page's 「AI 今日簡報」, the stats page's 「AI 弱點分析」): pressing it
 * opens Studio AI beside the page with `ask` sent in a new conversation. Nothing is asked until it is pressed — it is
 * an AI turn, charged like any other message — and a page shows it only to a signed-in reader, as Studio AI is.
 */
export default function AskStudioCard({ icon, title, description, ask }: AskStudioCardProps) {
  return (
    <Card
      elevation={0}
      sx={{
        mb: { xs: 3, md: 4 },
        borderRadius: 3,
        p: '1px',
        backgroundImage: ACCENT_GRADIENT,
      }}
    >
      <CardActionArea
        onClick={() => askStudio(ask, { fresh: true })}
        sx={{
          borderRadius: 'inherit',
          bgcolor: 'background.paper',
          p: { xs: 2, sm: 2.5 },
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'flex-start',
          gap: 2,
        }}
      >
        <Box
          sx={{
            flexShrink: 0,
            width: 44,
            height: 44,
            borderRadius: '50%',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: '#fff',
            backgroundImage: ACCENT_GRADIENT,
          }}
        >
          {icon}
        </Box>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 700, lineHeight: 1.3 }}>
            {title}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {description}
          </Typography>
        </Box>
      </CardActionArea>
    </Card>
  );
}
