import { Box, LinearProgress, Typography } from '@mui/material';
import SmartToyIcon from '@mui/icons-material/SmartToy';
import { StreamingStatus } from '@/hooks/useChatStreaming';

interface ProgressIndicatorProps {
  streamingStatus: StreamingStatus;
}

const ProgressIndicator = ({ streamingStatus }: ProgressIndicatorProps) => {
  if (!streamingStatus.isLoading) return null;

  return (
    <Box sx={{ px: 2, py: 1, borderBottom: '1px solid', borderColor: 'divider' }}>
      <LinearProgress sx={{ mb: 1, height: 4, borderRadius: 3 }} />
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{ display: 'flex', alignItems: 'center' }}
      >
        <SmartToyIcon sx={{ fontSize: 14, mr: 0.5 }} />
        {streamingStatus.statusText}
      </Typography>
    </Box>
  );
};

export default ProgressIndicator;
