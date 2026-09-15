import RecordVoiceOverIcon from '@mui/icons-material/RecordVoiceOver';
import { Alert, Chip, Stack, Typography } from '@mui/material';

import { DeliveryFeedback } from '@/apis/examApis/types';

interface DeliveryFeedbackAlertProps {
  feedback: DeliveryFeedback;
}

function DeliveryFeedbackAlert({ feedback }: DeliveryFeedbackAlertProps) {
  return (
    <Alert severity="success" icon={<RecordVoiceOverIcon fontSize="inherit" />} sx={{ mb: 2 }}>
      <Typography variant="body2" sx={{ fontWeight: 'bold', mb: 0.5 }}>
        表達流暢度回饋
      </Typography>
      <Stack direction="row" spacing={1} sx={{ mb: 0.5, flexWrap: 'wrap', rowGap: 0.5 }}>
        <Chip size="small" label={`語速 ${feedback.paceWpm} 字/分`} />
        <Chip size="small" label={`停頓 ${feedback.pauseCount} 次`} />
        <Chip size="small" label={`贅字 ${feedback.fillerWordCount} 次`} />
      </Stack>
      {feedback.comments.map((comment, index) => (
        <Typography key={index} variant="body2">
          {comment}
        </Typography>
      ))}
    </Alert>
  );
}

export default DeliveryFeedbackAlert;
