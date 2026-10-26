import HeadphonesIcon from '@mui/icons-material/HeadphonesOutlined';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import Typography from '@mui/material/Typography';

import { askStudio } from './askStudio';

import type { ReactNode } from 'react';

import { synthesisOf } from '@/components/aiChat/hooks/useSpeechOutput';
import { ACCENT_GRADIENT } from '@/components/aiChat/style';

export interface AskStudioCardProps {
  icon: ReactNode;
  title: string;
  description: string;
  /** What Studio AI is asked when the card is pressed, always as a new conversation: it stands on its own. */
  ask: string;
  /**
   * Also offer 「用聽的」: the same request, its answer read aloud once done with the browser's own voice (the
   * briefing, D-120). Only where the browser can speak.
   */
  listen?: boolean;
}

/**
 * A page's one-press way into Studio AI (the home page's 「AI 今日簡報」, the stats page's 「AI 弱點分析」): pressing it
 * opens Studio AI beside the page with `ask` sent in a new conversation. Nothing is asked until it is pressed — it is
 * an AI turn, charged like any other message — and a page shows it only to a signed-in reader, as Studio AI is.
 */
export default function AskStudioCard({
  icon,
  title,
  description,
  ask,
  listen = false,
}: AskStudioCardProps) {
  // Its own button beside the card's (a button cannot hold another).
  const canListen = listen && synthesisOf() !== null;
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
      <Box
        sx={{
          borderRadius: 'inherit',
          bgcolor: 'background.paper',
          display: 'flex',
          alignItems: 'center',
        }}
      >
        <CardActionArea
          onClick={() => askStudio(ask, { fresh: true })}
          sx={{
            flex: 1,
            minWidth: 0,
            borderRadius: 'inherit',
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
        {canListen && (
          <Button
            size="small"
            variant="outlined"
            color="inherit"
            startIcon={<HeadphonesIcon />}
            aria-label={`${title}（用聽的）`}
            onClick={() => askStudio(ask, { fresh: true, readAloud: true })}
            sx={{
              flexShrink: 0,
              mr: { xs: 1.5, sm: 2.5 },
              borderRadius: 99,
              whiteSpace: 'nowrap',
              color: 'text.secondary',
              borderColor: 'divider',
              // A phone keeps the card's words room: the headphones alone, named by aria-label.
              minWidth: { xs: 0, sm: 64 },
              px: { xs: 1, sm: 1.5 },
              '& .MuiButton-startIcon': { mr: { xs: 0, sm: 1 }, ml: { xs: 0, sm: -0.5 } },
            }}
          >
            <Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}>
              用聽的
            </Box>
          </Button>
        )}
      </Box>
    </Card>
  );
}
