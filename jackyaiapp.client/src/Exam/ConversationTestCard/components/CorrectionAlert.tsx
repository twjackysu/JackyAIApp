import { Alert, Typography } from '@mui/material';

import { ConversationCorrection } from '@/apis/examApis/types';

interface CorrectionAlertProps {
  correction: ConversationCorrection;
}

function CorrectionAlert({ correction }: CorrectionAlertProps) {
  if (!correction.hasCorrection) {
    return null;
  }

  return (
    <Alert severity="info" sx={{ mb: 2 }}>
      <Typography variant="body2">
        💡 <strong>建議修正:</strong> "{correction.originalText}" → "{correction.suggestedText}"
      </Typography>
      <Typography variant="body2" sx={{ mt: 0.5 }}>
        {correction.explanation}
      </Typography>
    </Alert>
  );
}

export default CorrectionAlert;
