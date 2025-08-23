import { AppBar, Avatar, Chip, IconButton, Stack, Toolbar, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { ConversationContext } from '@/apis/examApis/types';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import SmartToyIcon from '@mui/icons-material/SmartToy';

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
    <>
      <AppBar 
        position="static" 
        color="inherit" 
        elevation={0} 
        sx={{ borderBottom: '1px solid', borderColor: 'divider' }}
      >
        <Toolbar>
          <Avatar 
            sx={{ 
              mr: 2,
              bgcolor: theme.palette.success.main
            }}
          >
            <SmartToyIcon />
          </Avatar>
          <Stack>
            <Typography variant="h6" component="div">
              {context.scenario}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              AI 助手 - {context.aiRole}
            </Typography>
          </Stack>
          <Stack sx={{ flexGrow: 1 }} /> 
          <IconButton color="inherit">
            <MoreVertIcon />
          </IconButton>
        </Toolbar>
      </AppBar>
      
      <Stack 
        direction="row" 
        spacing={1} 
        flexWrap="wrap" 
        sx={{ 
          p: 2, 
          bgcolor: theme.palette.mode === 'dark' 
            ? 'rgba(144, 202, 249, 0.08)' 
            : 'rgba(144, 202, 249, 0.12)',
          borderBottom: '1px solid',
          borderColor: 'divider'
        }}
      >
        <Chip
          label={`你的角色: ${context.userRole}`}
          variant="outlined"
          color="primary"
          size="small"
        />
        <Chip
          label={`難度: ${DIFFICULTY_LABELS[difficultyLevel as keyof typeof DIFFICULTY_LABELS] || '中級'}`}
          variant="outlined"
          color="success"
          size="small"
        />
      </Stack>
    </>
  );
}

export default ConversationHeader;