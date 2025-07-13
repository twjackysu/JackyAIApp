import { Chip, Paper, Stack, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { ConversationContext } from '@/apis/examApis/types';

interface ConversationHeaderProps {
  context: ConversationContext;
  difficultyLevel: number;
}

const DIFFICULTY_LABELS = {
  1: '初級',
  2: '初中級',
  3: '中級',
  4: '中高級',
  5: '高級',
} as const;

function ConversationHeader({ context, difficultyLevel }: ConversationHeaderProps) {
  const theme = useTheme();

  return (
    <Paper sx={{ 
      p: 2, 
      mb: 2, 
      bgcolor: theme.palette.mode === 'dark' ? 'rgba(144, 202, 249, 0.08)' : 'rgba(144, 202, 249, 0.12)'
    }}>
      <Typography variant="h6" gutterBottom>
        📍 {context.scenario}
      </Typography>
      <Stack direction="row" spacing={1} flexWrap="wrap">
        <Chip
          label={`你的角色: ${context.userRole}`}
          variant="outlined"
          color="primary"
        />
        <Chip
          label={`AI角色: ${context.aiRole}`}
          variant="outlined"
          color="secondary"
        />
        <Chip
          label={`難度: ${DIFFICULTY_LABELS[difficultyLevel as keyof typeof DIFFICULTY_LABELS] || '中級'}`}
          variant="outlined"
          color="success"
          size="small"
        />
      </Stack>
    </Paper>
  );
}

export default ConversationHeader;