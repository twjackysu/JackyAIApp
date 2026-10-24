import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import Typography from '@mui/material/Typography';

import { ACCENT_GRADIENT } from '@/components/aiChat/style';
import { BRIEFING_PROMPT, askStudio } from '@/components/StudioAssistant/askStudio';

/**
 * The home page's way into Studio AI for a signed-in reader: one click asks it for today's briefing
 * (the day's calendar, the words due, the portfolio, together), and the answer opens beside the page.
 * Nothing is asked until it is clicked: the briefing is an AI turn, charged like any other message.
 */
export default function BriefingCard() {
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
        onClick={() => askStudio(BRIEFING_PROMPT, { fresh: true })}
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
          <AutoAwesomeIcon />
        </Box>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="subtitle1" sx={{ fontWeight: 700, lineHeight: 1.3 }}>
            AI 今日簡報
          </Typography>
          <Typography variant="body2" color="text.secondary">
            今天的行程、該複習的單字、資產概況，Studio AI 一次整理好
          </Typography>
        </Box>
      </CardActionArea>
    </Card>
  );
}
